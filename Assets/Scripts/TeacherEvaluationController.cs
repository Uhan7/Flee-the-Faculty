using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TeacherEvaluationController : MonoBehaviour
{
    private const string MainMenuSceneName = "Main Menu";
    private const string MainMenuScenePath = "Assets/Scenes/Main Menu.unity";
    private const string TeacherLoadingTaskId = "teacher-evaluation";
    private const float ClassroomLoadTimeoutSeconds = 30f;

    private readonly List<EvaluationParticipant> participants = new List<EvaluationParticipant>();

    [SerializeField] private DialogueActor teacherActor;
    [SerializeField] private ClassroomDoorExitSequence doorExitSequence;

    [Header("Evaluation Progress")]
    [SerializeField] private EvaluationProgressView evaluationHud;
    [SerializeField, Min(0f)] private float cameraFocusLeadSeconds = 0.3f;
    [SerializeField] private Vector2 evaluationStudentLift = new Vector2(0f, 0.68f);

    private ClassroomSessionController classroomSession;
    private DialogueConversationCamera conversationCamera;
    private DialogueManager dialogueManager;
    private FleeApiClient apiClient;
    private FleeTeacherSceneResult teacherResult;
    private string statusMessage = "Preparing teacher evaluation...";
    private int evaluatedCount;
    private int passedCount;
    private bool isTeacherLoadingTaskActive;

    public int EvaluatedCount => evaluatedCount;
    public int PassedCount => passedCount;
    public int StudentCount => participants.Count;

    private IEnumerator Start()
    {
        isTeacherLoadingTaskActive = DoorSceneTransition.TryRegisterLoadingTask(
            TeacherLoadingTaskId,
            "Preparing the Teacher...",
            0f,
            3f);

        if (teacherActor == null)
        {
            teacherActor = GetComponent<DialogueActor>();
        }

        ConfigureEvaluationMode();
        UpdateTeacherLoadingTask(0.1f, "Setting up the classroom for the Teacher...");
        yield return WaitForClassroom();

        if (classroomSession == null || classroomSession.Classroom == null)
        {
            yield return AbortEvaluation("Teacher evaluation could not load the classroom.");
            yield break;
        }

        BuildParticipantList(classroomSession.Classroom);
        if (participants.Count == 0)
        {
            yield return AbortEvaluation("Teacher evaluation could not find any students.");
            yield break;
        }

        ResolveEvaluationHud();
        evaluationHud?.Initialize(participants.Count);

        dialogueManager = DialogueManager.GetOrCreate();
        if (dialogueManager == null)
        {
            yield return AbortEvaluation("Teacher evaluation could not start the dialogue system.");
            yield break;
        }

        apiClient = FleeApiClient.GetOrCreate();
        FleeApiFailure teacherFailure = null;
        statusMessage = "The Teacher is checking what the students learned...";
        UpdateTeacherLoadingTask(0.35f, "Waiting for the Teacher's response...");
        yield return apiClient.RunTeacherScene(
            result => teacherResult = result,
            error => teacherFailure = error,
            (progress, message) => UpdateTeacherLoadingTask(progress, message));

        if (teacherFailure != null || teacherResult == null)
        {
            string message = teacherFailure != null
                ? "Teacher evaluation failed: " + teacherFailure.Message
                : "Teacher evaluation did not return a result.";
            yield return AbortEvaluation(message);
            yield break;
        }

        if (!TryValidateTeacherResults(out string alignmentIssue))
        {
            Debug.LogWarning(
                alignmentIssue + " The evaluation will use the classroom's saved student state.",
                this);
        }

        yield return PreloadTeacherVoices();

        CompleteTeacherLoadingTask("The Teacher is ready.");
        statusMessage = "Evaluation starting...";
        yield return PlayDialogue(BuildPaginatedDialogue(
            "teacher-evaluation-introduction",
            teacherActor,
            "Teacher",
            "Alright, class. I will now evaluate what you learned today."));

        // The endpoint still finalizes the run, but its generated transfer prompt can
        // drift to another concept. Present only the state saved for each scene pupil.
        for (int index = 0; index < participants.Count; index++)
        {
            yield return EvaluateStudent(participants[index]);
        }

        int rescueQuota = classroomSession.Classroom.RescueQuota;
        statusMessage = "Evaluation complete: " + passedCount + " / " +
            rescueQuota + " rescued";
        string summary = BuildGroundedSummary(passedCount, rescueQuota);
        yield return PlayDialogue(BuildPaginatedDialogue(
            "teacher-evaluation-summary",
            teacherActor,
            "Teacher",
            summary));
    }

    private void ConfigureEvaluationMode()
    {
#if UNITY_2023_1_OR_NEWER
        classroomSession = FindFirstObjectByType<ClassroomSessionController>();
        conversationCamera = FindFirstObjectByType<DialogueConversationCamera>();
        AraBotClickToMove araBotMovement = FindFirstObjectByType<AraBotClickToMove>();
#else
        classroomSession = FindObjectOfType<ClassroomSessionController>();
        conversationCamera = FindObjectOfType<DialogueConversationCamera>();
        AraBotClickToMove araBotMovement = FindObjectOfType<AraBotClickToMove>();
#endif

        if (classroomSession != null)
        {
            classroomSession.SetConversationCounterVisible(false);
        }

        if (araBotMovement != null)
        {
            araBotMovement.gameObject.SetActive(false);
        }

        if (doorExitSequence == null)
        {
            doorExitSequence = ClassroomDoorExitSequence.FindOrCreate();
        }
    }

    private IEnumerator WaitForClassroom()
    {
        float deadline = Time.realtimeSinceStartup + ClassroomLoadTimeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (classroomSession == null)
            {
#if UNITY_2023_1_OR_NEWER
                classroomSession = FindFirstObjectByType<ClassroomSessionController>();
#else
                classroomSession = FindObjectOfType<ClassroomSessionController>();
#endif
                if (classroomSession != null)
                {
                    classroomSession.SetConversationCounterVisible(false);
                }
            }

            if (classroomSession != null
                && classroomSession.Classroom != null
                && classroomSession.SpawnedStudentCount > 0)
            {
                yield break;
            }

            yield return null;
        }
    }

    private void BuildParticipantList(FleeClassroomSession classroom)
    {
        participants.Clear();
        DialogueActor[] actors = classroomSession.GetComponentsInChildren<DialogueActor>(true);
        Dictionary<string, DialogueActor> actorsByName = new Dictionary<string, DialogueActor>(
            System.StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < actors.Length; index++)
        {
            DialogueActor actor = actors[index];
            if (actor == null)
            {
                continue;
            }

            actorsByName[actor.gameObject.name] = actor;
            actorsByName[actor.DisplayName] = actor;
        }

        for (int index = 0; index < classroom.Pupils.Length; index++)
        {
            FleePupilSession pupil = classroom.Pupils[index];
            if (pupil == null || !actorsByName.TryGetValue(pupil.Name, out DialogueActor actor))
            {
                Debug.LogWarning(
                    "Teacher evaluation could not find the scene object for " +
                    (pupil != null ? pupil.Name : "an unknown student") + ".",
                    this);
                continue;
            }

            GameObject studentRoot = ResolveStudentRoot(actor);
            DisableNormalStudentGameplay(studentRoot);
            ConfigureEvaluationStudent(studentRoot);
            participants.Add(new EvaluationParticipant(pupil, actor, studentRoot));
        }
    }

    private void ConfigureEvaluationStudent(GameObject studentRoot)
    {
        if (studentRoot == null)
        {
            return;
        }

        Transform studentTransform = studentRoot.transform;
        studentTransform.position += (Vector3)evaluationStudentLift;
    }

    private bool TryValidateTeacherResults(out string issue)
    {
        issue = null;
        if (teacherResult.Results.Length != participants.Count)
        {
            issue = "The Teacher's records did not match the students in this classroom. Please restart the classroom and try again.";
            return false;
        }

        HashSet<string> evaluatedPupils = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < teacherResult.Results.Length; index++)
        {
            FleeTeacherPupilResult result = teacherResult.Results[index];
            EvaluationParticipant participant = FindParticipant(result);
            string resultKey = result != null && !string.IsNullOrWhiteSpace(result.PupilId)
                ? result.PupilId
                : result != null ? result.Name : string.Empty;
            if (participant == null
                || string.IsNullOrWhiteSpace(resultKey)
                || !evaluatedPupils.Add(resultKey))
            {
                issue = "The Teacher returned mixed or incomplete student records. Please restart the classroom and try again.";
                return false;
            }

            string expectedMisconception = participant.Pupil.Misconception?.Trim() ?? string.Empty;
            string returnedMisconception = result.Misconception?.Trim() ?? string.Empty;
            if (!string.Equals(
                    expectedMisconception,
                    returnedMisconception,
                    System.StringComparison.Ordinal))
            {
                issue = "The Teacher received a misconception that did not belong to this classroom.";
                return false;
            }

            string expectedRestatement = participant.Pupil.LearnedAnswer?.Trim() ?? string.Empty;
            string returnedRestatement = result.Restatement?.Trim() ?? string.Empty;
            if (!string.Equals(expectedRestatement, returnedRestatement, System.StringComparison.Ordinal))
            {
                issue = "The Teacher received an older answer than the one shown in this classroom. Please restart the classroom and try again.";
                return false;
            }
        }

        return true;
    }

    private EvaluationParticipant FindParticipant(FleeTeacherPupilResult result)
    {
        if (result == null)
        {
            return null;
        }

        for (int index = 0; index < participants.Count; index++)
        {
            EvaluationParticipant participant = participants[index];
            bool idMatches = !string.IsNullOrWhiteSpace(result.PupilId)
                && string.Equals(
                    participant.Pupil.PupilId,
                    result.PupilId,
                    System.StringComparison.Ordinal);
            bool nameMatches = !string.IsNullOrWhiteSpace(result.Name)
                && string.Equals(
                    participant.Pupil.Name,
                    result.Name,
                    System.StringComparison.OrdinalIgnoreCase);
            if (idMatches || nameMatches)
            {
                return participant;
            }
        }

        return null;
    }

    private IEnumerator EvaluateStudent(EvaluationParticipant participant)
    {
        statusMessage = "Evaluating " + participant.Pupil.Name +
            " (" + (evaluatedCount + 1) + " / " + participants.Count + ")";

        if (conversationCamera != null)
        {
            conversationCamera.BeginExternalFocus(participant.Actor.transform, false);
            if (cameraFocusLeadSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(cameraFocusLeadSeconds);
            }
        }

        bool rescued = participant.Pupil.Satisfied
            && !string.IsNullOrWhiteSpace(participant.Pupil.LearnedAnswer);
        string teacherQuestion = BuildGroundedQuestion(participant.Pupil);
        string pupilAnswer = BuildGroundedAnswer(participant.Pupil);
        string teacherReaction = rescued
            ? "Very good, " + participant.Pupil.Name + ". You may leave the classroom."
            : "That is not quite right, " + participant.Pupil.Name + ". Please remain in the classroom.";

        List<IDialogueLine> evaluationLines = new List<IDialogueLine>();
        AddPaginatedLines(evaluationLines, teacherActor, "Teacher", teacherQuestion);
        AddPaginatedLines(
            evaluationLines,
            participant.Actor,
            participant.Pupil.Name,
            pupilAnswer);
        AddPaginatedLines(evaluationLines, teacherActor, "Teacher", teacherReaction);
        yield return PlayDialogue(new RuntimeDialogueSequence(
            "teacher-evaluation-" + participant.Pupil.PupilId,
            evaluationLines));

        evaluatedCount++;
        if (rescued)
        {
            passedCount++;
            if (conversationCamera != null)
            {
                conversationCamera.EndExternalFocus();
            }

            if (doorExitSequence != null)
            {
                yield return doorExitSequence.PlayStudentExit(participant.StudentRoot);
            }
            else
            {
                Debug.LogWarning(
                    "The classroom doors were not found, so the rescued student will leave immediately.",
                    this);
                participant.StudentRoot.SetActive(false);
            }
        }

        evaluationHud?.SetStudentResult(
            evaluatedCount - 1,
            rescued,
            evaluatedCount,
            participants.Count);

        if (conversationCamera != null)
        {
            conversationCamera.EndExternalFocus();
        }

        yield return null;
    }

    private IEnumerator PreloadTeacherVoices()
    {
        yield return DialogueVoicePreloader.Preload(BuildPaginatedDialogue(
            "teacher-evaluation-introduction",
            teacherActor,
            "Teacher",
            "Alright, class. I will now evaluate what you learned today."));

        int expectedRescued = 0;
        for (int index = 0; index < participants.Count; index++)
        {
            EvaluationParticipant participant = participants[index];
            bool rescued = participant.Pupil.Satisfied
                && !string.IsNullOrWhiteSpace(participant.Pupil.LearnedAnswer);
            if (rescued)
            {
                expectedRescued++;
            }
            string teacherQuestion = BuildGroundedQuestion(participant.Pupil);
            string pupilAnswer = BuildGroundedAnswer(participant.Pupil);
            string teacherReaction = rescued
                ? "Very good, " + participant.Pupil.Name + ". You may leave the classroom."
                : "That is not quite right, " + participant.Pupil.Name + ". Please remain in the classroom.";

            List<IDialogueLine> lines = new List<IDialogueLine>();
            AddPaginatedLines(lines, teacherActor, "Teacher", teacherQuestion);
            AddPaginatedLines(lines, participant.Actor, participant.Pupil.Name, pupilAnswer);
            AddPaginatedLines(lines, teacherActor, "Teacher", teacherReaction);
            yield return DialogueVoicePreloader.Preload(new RuntimeDialogueSequence(
                "teacher-evaluation-preload-" + participant.Pupil.PupilId,
                lines));
        }

        int rescueQuota = classroomSession.Classroom.RescueQuota;
        yield return DialogueVoicePreloader.Preload(BuildPaginatedDialogue(
            "teacher-evaluation-summary",
            teacherActor,
            "Teacher",
            BuildGroundedSummary(expectedRescued, rescueQuota)));
    }

    private static string BuildGroundedQuestion(FleePupilSession pupil)
    {
        string pupilName = string.IsNullOrWhiteSpace(pupil.Name) ? "Student" : pupil.Name.Trim();
        if (!string.IsNullOrWhiteSpace(pupil.Misconception))
        {
            string startingPoint = pupil.Misconception.Trim();
            return EndsAsQuestion(startingPoint)
                ? pupilName + ", earlier you asked: \"" + startingPoint +
                    "\" What answer did you learn from AraBOT?"
                : pupilName + ", you started with this idea: \"" + startingPoint +
                    "\" What did you learn instead?";
        }

        return pupilName + ", what did you learn from AraBOT?";
    }

    private static bool EndsAsQuestion(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && text.TrimEnd().EndsWith("?");
    }

    private static string BuildGroundedAnswer(FleePupilSession pupil)
    {
        return string.IsNullOrWhiteSpace(pupil.LearnedAnswer)
            ? "I don't know yet."
            : pupil.LearnedAnswer.Trim();
    }

    private static string BuildGroundedSummary(int rescued, int rescueQuota)
    {
        if (rescued >= rescueQuota)
        {
            return "AraBOT, that concludes the evaluation. " + rescued +
                " students understood what they were taught, so the class may go home.";
        }

        return "AraBOT, that concludes the evaluation. " + rescued + " of the required " +
            rescueQuota + " students understood what they were taught.";
    }

    private static RuntimeDialogueSequence BuildPaginatedDialogue(
        string conversationId,
        Object speakerReference,
        string speakerName,
        string text)
    {
        List<IDialogueLine> lines = new List<IDialogueLine>();
        AddPaginatedLines(lines, speakerReference, speakerName, text);
        return new RuntimeDialogueSequence(conversationId, lines);
    }

    private static void AddPaginatedLines(
        List<IDialogueLine> lines,
        Object speakerReference,
        string speakerName,
        string text)
    {
        IReadOnlyList<string> pages = DialogueTextPaginator.Split(text);
        for (int index = 0; index < pages.Count; index++)
        {
            lines.Add(new RuntimeDialogueLine(
                speakerReference,
                speakerName,
                pages[index]));
        }
    }

    private IEnumerator PlayDialogue(IDialogueSequence dialogue)
    {
        if (dialogueManager == null || dialogue == null || !dialogueManager.Play(dialogue))
        {
            yield break;
        }

        while (dialogueManager != null && dialogueManager.IsPlaying)
        {
            yield return null;
        }
    }

    private IEnumerator AbortEvaluation(string message)
    {
        CompleteTeacherLoadingTask("The Teacher could not get ready.");
        statusMessage = message;
        Debug.LogError(message, this);
        dialogueManager = DialogueManager.GetOrCreate();

        if (dialogueManager != null)
        {
            yield return PlayDialogue(BuildPaginatedDialogue(
                "teacher-evaluation-error",
                teacherActor,
                "Teacher",
                "The evaluation records are unavailable. We will return to the main menu."));
        }
        else
        {
            yield return new WaitForSecondsRealtime(2f);
        }

        DoorSceneTransition.LoadScene(MainMenuSceneName, MainMenuScenePath);
    }

    private void UpdateTeacherLoadingTask(float progress, string message)
    {
        if (isTeacherLoadingTaskActive)
        {
            DoorSceneTransition.UpdateLoadingTask(TeacherLoadingTaskId, progress, message);
        }
    }

    private void CompleteTeacherLoadingTask(string message)
    {
        if (!isTeacherLoadingTaskActive)
        {
            return;
        }

        DoorSceneTransition.CompleteLoadingTask(TeacherLoadingTaskId, message);
        isTeacherLoadingTaskActive = false;
    }

    private void ResolveEvaluationHud()
    {
        if (evaluationHud != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        evaluationHud = FindFirstObjectByType<EvaluationProgressView>(FindObjectsInactive.Include);
#else
        evaluationHud = FindObjectOfType<EvaluationProgressView>(true);
#endif
        if (evaluationHud == null)
        {
            Debug.LogError(
                "Teacher Evaluation is missing its editable Evaluation Progress HUD.",
                this);
        }
    }

    private static GameObject ResolveStudentRoot(DialogueActor actor)
    {
        StudentRoamingController roamingController = actor.GetComponentInParent<StudentRoamingController>();
        return roamingController != null ? roamingController.gameObject : actor.gameObject;
    }

    private static void DisableNormalStudentGameplay(GameObject studentRoot)
    {
        if (studentRoot == null)
        {
            return;
        }

        StudentRoamingController[] roamingControllers =
            studentRoot.GetComponentsInChildren<StudentRoamingController>(true);
        for (int index = 0; index < roamingControllers.Length; index++)
        {
            roamingControllers[index].enabled = false;
        }

        StudentDialogueInteraction[] interactions =
            studentRoot.GetComponentsInChildren<StudentDialogueInteraction>(true);
        for (int index = 0; index < interactions.Length; index++)
        {
            interactions[index].enabled = false;
        }
    }

    private sealed class EvaluationParticipant
    {
        public EvaluationParticipant(FleePupilSession pupil, DialogueActor actor, GameObject studentRoot)
        {
            Pupil = pupil;
            Actor = actor;
            StudentRoot = studentRoot;
        }

        public FleePupilSession Pupil { get; }
        public DialogueActor Actor { get; }
        public GameObject StudentRoot { get; }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class TeacherEvaluationController : MonoBehaviour
{
    private const string EvaluationSceneName = "Teacher Evaluation";
    private const string EvaluationControllerPrefabPath = "Teacher Evaluation Sequence";
    private const string MainMenuSceneName = "Main Menu";
    private const string MainMenuScenePath = "Assets/Scenes/Main Menu.unity";
    private const string TeacherLoadingTaskId = "teacher-evaluation";
    private const float ClassroomLoadTimeoutSeconds = 30f;

    private readonly List<EvaluationParticipant> participants = new List<EvaluationParticipant>();

    [SerializeField] private DialogueActor teacherActor;
    [SerializeField] private ClassroomDoorExitSequence doorExitSequence;

    private ClassroomSessionController classroomSession;
    private DialogueConversationCamera conversationCamera;
    private DialogueManager dialogueManager;
    private FleeApiClient apiClient;
    private FleeTeacherSceneResult teacherResult;
    private GUIStyle progressStyle;
    private string statusMessage = "Preparing teacher evaluation...";
    private int evaluatedCount;
    private int passedCount;
    private bool isTeacherLoadingTaskActive;

    public int EvaluatedCount => evaluatedCount;
    public int PassedCount => passedCount;
    public int StudentCount => participants.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterEvaluationSceneBootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        if (!string.Equals(scene.name, EvaluationSceneName))
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        TeacherEvaluationController existingController = FindFirstObjectByType<TeacherEvaluationController>();
#else
        TeacherEvaluationController existingController = FindObjectOfType<TeacherEvaluationController>();
#endif
        if (existingController == null)
        {
            GameObject controllerPrefab = Resources.Load<GameObject>(EvaluationControllerPrefabPath);
            if (controllerPrefab == null)
            {
                Debug.LogError(
                    "Teacher evaluation controller prefab is missing from Resources/" +
                    EvaluationControllerPrefabPath + ".prefab.");
                return;
            }

            GameObject controllerInstance = Instantiate(controllerPrefab);
            controllerInstance.name = controllerPrefab.name;
        }
    }

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
            error => teacherFailure = error);

        if (teacherFailure != null || teacherResult == null)
        {
            string message = teacherFailure != null
                ? "Teacher evaluation failed: " + teacherFailure.Message
                : "Teacher evaluation did not return a result.";
            yield return AbortEvaluation(message);
            yield break;
        }

        CompleteTeacherLoadingTask("The Teacher is ready.");
        statusMessage = "Evaluation starting...";
        yield return PlayDialogue(BuildPaginatedDialogue(
            "teacher-evaluation-introduction",
            teacherActor,
            "Teacher",
            "Alright, class. I will now evaluate what you learned today."));

        for (int index = 0; index < teacherResult.Results.Length; index++)
        {
            FleeTeacherPupilResult pupilResult = teacherResult.Results[index];
            EvaluationParticipant participant = FindParticipant(pupilResult);
            if (participant == null)
            {
                Debug.LogWarning(
                    "Teacher evaluation could not find the scene object for " +
                    (pupilResult != null ? pupilResult.Name : "an unknown student") + ".",
                    this);
                continue;
            }

            yield return EvaluateStudent(participant, pupilResult);
        }

        passedCount = teacherResult.Rescued;
        statusMessage = "Evaluation complete: " + teacherResult.Rescued + " / " +
            teacherResult.RescueQuota + " rescued";
        string summary = string.IsNullOrWhiteSpace(teacherResult.TeacherRemark)
            ? BuildFallbackSummary(teacherResult)
            : teacherResult.TeacherRemark.Trim();
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
            participants.Add(new EvaluationParticipant(pupil, actor, studentRoot));
        }
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

    private IEnumerator EvaluateStudent(
        EvaluationParticipant participant,
        FleeTeacherPupilResult result)
    {
        statusMessage = "Evaluating " + participant.Pupil.Name +
            " (" + (evaluatedCount + 1) + " / " + teacherResult.Results.Length + ")";

        if (conversationCamera != null)
        {
            conversationCamera.BeginExternalFocus(participant.Actor.transform);
        }

        bool rescued = result.Rescued;
        string transferQuestion = string.IsNullOrWhiteSpace(result.TransferQuestion)
            ? participant.Pupil.Name + ", can you apply what you learned?"
            : result.TransferQuestion.Trim();
        string pupilAnswer = string.IsNullOrWhiteSpace(result.PupilAnswer)
            ? "I don't know yet."
            : result.PupilAnswer.Trim();
        string teacherReaction = rescued
            ? "Very good, " + participant.Pupil.Name + ". You may leave the classroom."
            : "That is not quite right, " + participant.Pupil.Name + ". Please remain in the classroom.";

        List<IDialogueLine> evaluationLines = new List<IDialogueLine>();
        AddPaginatedLines(evaluationLines, teacherActor, "Teacher", transferQuestion);
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

        if (conversationCamera != null)
        {
            conversationCamera.EndExternalFocus();
        }

        yield return null;
    }

    private static string BuildFallbackSummary(FleeTeacherSceneResult result)
    {
        if (result.Cleared)
        {
            return "That concludes the evaluation. " + result.Rescued +
                " students are rescued, so the class may go home.";
        }

        return "That concludes the evaluation. " + result.Rescued + " of the required " +
            result.RescueQuota + " students were rescued.";
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

    private void OnGUI()
    {
        if (progressStyle == null)
        {
            progressStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 18,
                padding = new RectOffset(12, 12, 8, 8)
            };
        }

        Vector2 size = progressStyle.CalcSize(new GUIContent(statusMessage));
        GUI.Box(new Rect(16f, 16f, size.x, size.y), statusMessage, progressStyle);
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

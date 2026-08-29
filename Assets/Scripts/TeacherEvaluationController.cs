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
    private const float ClassroomLoadTimeoutSeconds = 30f;

    private readonly List<EvaluationParticipant> participants = new List<EvaluationParticipant>();

    [SerializeField] private DialogueActor teacherActor;

    private ClassroomSessionController classroomSession;
    private DialogueConversationCamera conversationCamera;
    private DialogueManager dialogueManager;
    private GUIStyle progressStyle;
    private string statusMessage = "Preparing teacher evaluation...";
    private int evaluatedCount;
    private int passedCount;

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
        if (teacherActor == null)
        {
            teacherActor = GetComponent<DialogueActor>();
        }

        ConfigureEvaluationMode();
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

        statusMessage = "Evaluation starting...";
        yield return PlayDialogue(new RuntimeDialogueSequence(
            "teacher-evaluation-introduction",
            new[]
            {
                new RuntimeDialogueLine(
                    teacherActor,
                    "Teacher",
                    "Alright, class. I will now evaluate what you learned today.")
            }));

        for (int index = 0; index < participants.Count; index++)
        {
            yield return EvaluateStudent(participants[index], classroomSession.Classroom.Topic);
        }

        statusMessage = "Evaluation complete: " + passedCount + " / " + participants.Count + " passed";
        yield return PlayDialogue(new RuntimeDialogueSequence(
            "teacher-evaluation-summary",
            new[]
            {
                new RuntimeDialogueLine(
                    teacherActor,
                    "Teacher",
                    "That concludes the evaluation. " + passedCount + " out of " + participants.Count +
                    " students may leave the classroom.")
            }));
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

    private IEnumerator EvaluateStudent(EvaluationParticipant participant, string topic)
    {
        statusMessage = "Evaluating " + participant.Pupil.Name +
            " (" + (evaluatedCount + 1) + " / " + participants.Count + ")";

        if (conversationCamera != null)
        {
            conversationCamera.BeginExternalFocus(participant.Actor.transform);
        }

        bool passed = participant.Pupil.Satisfied;
        string safeTopic = string.IsNullOrWhiteSpace(topic) ? "today's lesson" : topic.Trim();
        string studentAnswer = BuildStudentAnswer(participant.Pupil);
        string teacherReaction = passed
            ? "Very good, " + participant.Pupil.Name + ". You may leave the classroom."
            : "That is not quite right, " + participant.Pupil.Name + ". Please remain in the classroom.";

        yield return PlayDialogue(new RuntimeDialogueSequence(
            "teacher-evaluation-" + participant.Pupil.PupilId,
            new IDialogueLine[]
            {
                new RuntimeDialogueLine(
                    teacherActor,
                    "Teacher",
                    participant.Pupil.Name + ", what did you learn about " + safeTopic + "?"),
                new RuntimeDialogueLine(
                    participant.Actor,
                    participant.Pupil.Name,
                    studentAnswer),
                new RuntimeDialogueLine(
                    teacherActor,
                    "Teacher",
                    teacherReaction)
            }));

        evaluatedCount++;
        if (passed)
        {
            passedCount++;
            participant.StudentRoot.SetActive(false);
        }

        if (conversationCamera != null)
        {
            conversationCamera.EndExternalFocus();
        }

        yield return null;
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
        statusMessage = message;
        Debug.LogError(message, this);
        dialogueManager = DialogueManager.GetOrCreate();

        if (dialogueManager != null)
        {
            yield return PlayDialogue(new RuntimeDialogueSequence(
                "teacher-evaluation-error",
                new[]
                {
                    new RuntimeDialogueLine(
                        teacherActor,
                        "Teacher",
                        "The evaluation records are unavailable. We will return to the main menu.")
                }));
        }
        else
        {
            yield return new WaitForSecondsRealtime(2f);
        }

        DoorSceneTransition.LoadScene(MainMenuSceneName, MainMenuScenePath);
    }

    private static string BuildStudentAnswer(FleePupilSession pupil)
    {
        if (!string.IsNullOrWhiteSpace(pupil.LearnedAnswer))
        {
            return pupil.LearnedAnswer.Trim();
        }

        if (!pupil.Satisfied && !string.IsNullOrWhiteSpace(pupil.Misconception))
        {
            return "I'm still not sure. I think " + pupil.Misconception.Trim();
        }

        return pupil.Satisfied
            ? "I understand the lesson now."
            : "I don't know yet.";
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

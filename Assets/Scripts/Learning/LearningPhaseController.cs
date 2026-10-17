using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Where the Learning phase is. Read it for debugging or a test; nothing in the
/// game branches on it from outside.
/// </summary>
public enum LearningPhaseState
{
    Starting = 0,
    PreparingClassroom = 1,
    LoadingLesson = 2,

    /// <summary>The Teacher is talking through a step.</summary>
    Teaching = 3,

    /// <summary>The step is done and Continue Lesson is up.</summary>
    AwaitingContinue = 4,

    /// <summary>AraBOT asked something and she is answering it.</summary>
    AraBotQuestion = 5,

    /// <summary>The Teacher put a Comprehension Check and she is answering it.</summary>
    ComprehensionCheck = 6,

    /// <summary>She pressed the mic and is asking the Teacher something.</summary>
    Interjection = 7,

    Closing = 8,
    Leaving = 9
}

/// <summary>
/// The Learning phase: the Teacher teaches AraBOT a Lesson, with Ara beside
/// AraBOT, before the Classroom opens. This is the whole phase in one place, a
/// state machine driven by one coroutine.
///
/// The flow:
///  1. Build the Classroom (or wait for it), fetch the Lesson. No Lesson, a
///     failed call or a Classroom that already had its Learning phase means
///     straight to "Classroom and Movement". Learning never blocks the game.
///  2. For each step: show its whiteboard, play its Teacher lines, then (once
///     per Lesson) AraBOT's Question if it has one, then (once per section) the
///     section's Comprehension Checks, then Continue Lesson. Nothing advances
///     without that button. Back goes to the previous step.
///  3. The mic works at any point. A Learner Interjection pauses the Lesson,
///     asks the Teacher, plays the answer and a replay of the step the service
///     chose, then resumes on the line she left.
///  4. After the last step AraBOT says a closing line and the Classroom opens.
///
/// What it deliberately does not do: grade anything, call a model (the service
/// does, for the Interjection only), log or keep what she says, or show a
/// Pupil speaking. Her words go to the service and nowhere else.
///
/// Where the pieces live: the cursor and the pure helpers are in
/// Flee.Learning.Core and are tested; the whiteboard and the buttons are the
/// views from the first Learning change; speech capture and the dialogue box are
/// the existing flows, reused unchanged.
///
/// How it gets into the scene: the builder (Flee/Build Learning Scene) adds a
/// "Learning Sequence" object carrying this component. If the scene is called
/// "Learning" and has none, one is created at load, the same as the classroom
/// picker is added to the main menu.
/// </summary>
[DisallowMultipleComponent]
public sealed class LearningPhaseController : MonoBehaviour
{
    public const string LearningSceneName = "Learning";
    public const string LearningScenePath = "Assets/Scenes/Learning.unity";
    public const string ClassroomSceneName = "Classroom and Movement";
    public const string ClassroomScenePath = "Assets/Scenes/Classroom and Movement.unity";

    private const string LoadingTaskId = "learning-phase";
    private const string TeacherName = "Teacher";
    private const string AraBotName = "AraBOT";
    private const string LearnerName = "Ara";
    private const string AskTitle = "Ask the Teacher";

    private static readonly LearningRunLedger RunLedger = new LearningRunLedger();

    [Header("Cast")]
    [Tooltip("The Teacher's DialogueActor. The builder puts one on this object. "
        + "Empty is allowed: the Teacher then speaks with no voice ticks.")]
    [SerializeField] private DialogueActor teacherActor;
    [Tooltip("AraBOT's DialogueActor. Found by its display name when empty.")]
    [SerializeField] private DialogueActor araBotActor;

    [Header("Classroom")]
    [Tooltip("Build the Classroom here and spawn no Pupils. A Pupil that spawns starts "
        + "an Encounter on the service, which renders speech ahead of time, and the "
        + "Classroom scene would then do the same again. Turn off to leave the "
        + "Classroom to a ClassroomSessionController in this scene and wait for it.")]
    [SerializeField] private bool buildClassroomHere = true;
    [SerializeField, Min(1f)] private float classroomLoadTimeoutSeconds = 30f;

    [Header("Layout")]
    [SerializeField] private Vector2 canvasReferenceResolution = new Vector2(1600f, 900f);
    [SerializeField] private Vector2 whiteboardSize = new Vector2(640f, 300f);
    [SerializeField] private Vector2 whiteboardPosition = new Vector2(0f, -40f);

    private readonly LessonProgress progress = new LessonProgress();

    private LearningPhaseState phase = LearningPhaseState.Starting;
    private ClassroomSessionController classroomSession;
    private DialogueManager dialogueManager;
    private FleeApiClient apiClient;
    private DialogueSpeechCaptureFlow captureFlow;
    private LessonCursor cursor;
    private LessonWhiteboardView whiteboard;
    private LessonControlsView controls;
    private List<LessonSpeechPage> activePages;

    private string classroomId = string.Empty;
    private string language = "en";
    private string topic = string.Empty;
    private bool micAvailable;
    private bool trackLines;
    private bool isLeaving;
    private bool isLoadingTaskActive;
    private int sequenceCounter;

    private PendingIntent intent = PendingIntent.None;
    private PendingIntent endChoice = PendingIntent.None;
    private bool cancelRequested;
    private CaptureState captureState = CaptureState.Idle;
    private string capturedText = string.Empty;

    private enum PendingIntent
    {
        None = 0,
        Continue = 1,
        Back = 2,
        Mic = 3
    }

    private enum CaptureState
    {
        Idle = 0,
        Waiting = 1,
        Submitted = 2,
        Cancelled = 3
    }

    public LearningPhaseState State => phase;

    /// <summary>
    /// True once this Classroom has had its Learning phase this session, which is
    /// what makes a replay of a Classroom skip it.
    /// </summary>
    public static bool HasRun(string classroomId)
    {
        return RunLedger.HasRun(classroomId);
    }

    /// <summary>
    /// The scene Play should open: Learning once its scene is in the build, the
    /// Classroom until then. A caller that wants Learning in the flow asks this
    /// instead of naming either scene, so the game keeps working in a checkout
    /// where the scene has not been built yet.
    /// </summary>
    public static void ResolveEntryScene(out string sceneName, out string scenePath)
    {
        if (Application.CanStreamedLevelBeLoaded(LearningSceneName))
        {
            sceneName = LearningSceneName;
            scenePath = LearningScenePath;
            return;
        }

        sceneName = ClassroomSceneName;
        scenePath = ClassroomScenePath;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        RunLedger.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneBootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapActiveScene()
    {
        EnsureForScene(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        EnsureForScene(scene);
    }

    private static void EnsureForScene(Scene scene)
    {
        if (!string.Equals(scene.name, LearningSceneName, StringComparison.Ordinal)
            || FindFirst<LearningPhaseController>(true) != null)
        {
            return;
        }

        new GameObject("Learning Sequence").AddComponent<LearningPhaseController>();
    }

    private void Awake()
    {
        ResolveActors();

        // A ClassroomSessionController that has not started yet would build its
        // own Classroom and spawn Pupils, and two builders would race for one
        // Classroom. This runs before any Start, so switching it off here keeps
        // it from ever starting. The builder also leaves one out of the scene.
        if (buildClassroomHere)
        {
            ClassroomSessionController existing = FindFirst<ClassroomSessionController>(true);
            if (existing != null)
            {
                existing.enabled = false;
            }
        }
    }

    private IEnumerator Start()
    {
        yield return RunGuarded(Run());

        // However Run ended, she is never left in a scene with nothing to do.
        LeaveToClassroom();
    }

    private void OnDestroy()
    {
        if (dialogueManager != null)
        {
            dialogueManager.LineChanged -= HandleLineChanged;
        }

        if (controls != null)
        {
            controls.ContinueClicked -= HandleContinueClicked;
            controls.BackClicked -= HandleBackClicked;
            controls.MicClicked -= HandleMicClicked;
        }
    }

    // ----- The phase -----------------------------------------------------

    private IEnumerator Run()
    {
        phase = LearningPhaseState.Starting;
        isLoadingTaskActive = DoorSceneTransition.TryRegisterLoadingTask(
            LoadingTaskId,
            "Preparing the Lesson...",
            0f,
            3f);

        ConfigureScene();

        dialogueManager = DialogueManager.GetOrCreate();
        if (dialogueManager == null)
        {
            Debug.LogWarning("Learning could not start the dialogue system, so it is skipped.", this);
            yield break;
        }

        dialogueManager.LineChanged += HandleLineChanged;
        apiClient = FleeApiClient.GetOrCreate();

        phase = LearningPhaseState.PreparingClassroom;
        FleeClassroomSession classroom = null;
        yield return ObtainClassroom(value => classroom = value);
        if (classroom == null)
        {
            Debug.LogWarning("Learning has no Classroom to teach, so it is skipped.", this);
            yield break;
        }

        classroomId = classroom.ClassroomId ?? string.Empty;
        if (RunLedger.HasRun(classroomId))
        {
            // A replay of a Classroom: she was taught this one already.
            CompleteLoadingTask("Back to the Classroom.");
            yield break;
        }

        phase = LearningPhaseState.LoadingLesson;
        UpdateLoadingTask(0.8f, "Fetching the Lesson...");
        FleeLessonView lesson = null;
        FleeApiFailure failure = null;
        yield return apiClient.GetLesson(
            classroomId,
            value => lesson = value,
            error => failure = error);

        CompleteLoadingTask("The Teacher is ready.");
        if (lesson == null)
        {
            if (failure != null)
            {
                Debug.LogWarning("Learning could not fetch the Lesson: " + failure.Message, this);
            }
            else
            {
                // The Classroom has no Lesson. Nothing to teach is not a failure,
                // and a replay of it has nothing to teach either.
                RunLedger.MarkRun(classroomId);
            }

            yield break;
        }

        language = FleeClassroomLanguages.ToWireValue(
            FleeClassroomLanguages.FromWireValue(lesson.language));
        topic = !string.IsNullOrWhiteSpace(lesson.topic) ? lesson.topic : classroom.Topic;
        if (string.IsNullOrWhiteSpace(lesson.classroomId))
        {
            lesson.classroomId = classroomId;
        }

        // The route to the service is there when the Lesson came from it, and
        // the mock stands in for it. Either way the mic has something to ask.
        micAvailable = true;

        BuildInterface();
        RunLedger.MarkRun(classroomId);
        yield return Teach(lesson);
    }

    private IEnumerator Teach(FleeLessonView lesson)
    {
        cursor = new LessonCursor(lesson);
        if (!cursor.HasSteps)
        {
            yield break;
        }

        whiteboard.SetVisible(true);
        int startLine = 0;
        bool presentStep = true;

        while (true)
        {
            if (presentStep)
            {
                whiteboard.Show(cursor.CurrentWhiteboard);
                cursor.SetLine(startLine);
            }

            // 1. The Teacher talks through the step.
            phase = LearningPhaseState.Teaching;
            intent = PendingIntent.None;
            ApplyControls(false, cursor.CanGoBack, micAvailable, null);
            yield return PlayStepLines(startLine);

            if (intent == PendingIntent.Mic)
            {
                intent = PendingIntent.None;
                yield return RunInterjection();
                startLine = cursor.LineIndex;
                presentStep = false;
                continue;
            }

            if (intent == PendingIntent.Back)
            {
                intent = PendingIntent.None;
                if (cursor.Back())
                {
                    startLine = 0;
                    presentStep = true;
                }
                else
                {
                    presentStep = false;
                    startLine = cursor.LineIndex;
                }

                continue;
            }

            // 2. The end of the step: AraBOT's Question, then the section's checks.
            FleeAraBotQuestion question = cursor.CurrentAraBotQuestion;
            if (question != null && progress.ShouldAskQuestion(question.questionId))
            {
                progress.MarkQuestionAsked(question.questionId);
                yield return RunAraBotQuestion(question);
            }

            if (cursor.IsLastStepOfSection)
            {
                string sectionKey = LessonProgress.SectionKey(
                    cursor.CurrentSection != null ? cursor.CurrentSection.sectionId : null,
                    cursor.SectionIndex);
                if (cursor.CurrentChecks.Count > 0 && progress.ShouldRunChecks(sectionKey))
                {
                    progress.MarkChecksRun(sectionKey);
                    yield return RunChecks();
                }
            }

            // 3. Nothing moves until she says so.
            yield return AwaitEndOfStep();

            if (endChoice == PendingIntent.Back)
            {
                if (cursor.Back())
                {
                    startLine = 0;
                    presentStep = true;
                }
                else
                {
                    presentStep = false;
                }

                continue;
            }

            LessonMove move = cursor.NextStep();
            if (move == LessonMove.Finished)
            {
                break;
            }

            startLine = 0;
            presentStep = true;
        }

        phase = LearningPhaseState.Closing;
        ApplyControls(false, false, false, null);
        yield return Say(LessonSpeaker.AraBot, LessonLocalLines.ClosingLine(language, topic));
    }

    private IEnumerator PlayStepLines(int startLine)
    {
        List<LessonSpeechPage> pages = LessonSpeechPlan.Build(
            cursor.CurrentLines,
            startLine,
            LessonSpeaker.Teacher,
            SplitText);
        if (pages.Count == 0)
        {
            yield break;
        }

        trackLines = true;
        yield return PlayPages(pages);
        trackLines = false;
    }

    /// <summary>
    /// Continue Lesson is up and nothing plays. Back and the mic still work: a
    /// Learner Interjection from here comes back to this same screen.
    /// </summary>
    private IEnumerator AwaitEndOfStep()
    {
        while (true)
        {
            phase = LearningPhaseState.AwaitingContinue;
            intent = PendingIntent.None;
            ApplyControls(true, cursor.CanGoBack, micAvailable, null);

            while (intent == PendingIntent.None)
            {
                yield return null;
            }

            PendingIntent chosen = intent;
            intent = PendingIntent.None;
            ApplyControls(false, false, false, null);

            if (chosen == PendingIntent.Mic)
            {
                yield return RunInterjection();
                continue;
            }

            endChoice = chosen;
            yield break;
        }
    }

    // ----- Learner Interjection ------------------------------------------

    private IEnumerator RunInterjection()
    {
        phase = LearningPhaseState.Interjection;
        string stepId = cursor.CurrentStepId;

        yield return CaptureText(AskTitle, LessonLocalLines.AskInstructions(language));
        if (captureState != CaptureState.Submitted)
        {
            // She changed her mind. Nothing was asked, so there is nothing to undo.
            ClearCapturedText();
            yield break;
        }

        ApplyControls(false, false, false, null);
        EnsureCaptureFlow().ShowProcessing(TeacherName);

        FleeLessonQuestionResponse reply = null;
        FleeApiFailure failure = null;
        string question = capturedText;
        ClearCapturedText();
        yield return apiClient.AskTeacher(
            classroomId,
            stepId,
            question,
            value => reply = value,
            error => failure = error);
        EnsureCaptureFlow().Hide();

        if (reply == null || failure != null)
        {
            yield return Say(LessonSpeaker.Teacher, LessonLocalLines.InterjectionFailed(language));
            yield break;
        }

        yield return Say(LessonSpeaker.Teacher, reply.teacherLine);
        if (reply.HasReplay)
        {
            yield return ReplayStep(reply.replayStepId);
        }
    }

    /// <summary>
    /// Show another step again: its board and its lines, in the Teacher's voice.
    /// The cursor remembers where she was, so ending the replay puts both the
    /// board and the line back, and nothing is restarted or skipped.
    /// </summary>
    private IEnumerator ReplayStep(string stepId)
    {
        if (!cursor.BeginReplay(stepId))
        {
            yield break;
        }

        trackLines = false;
        whiteboard.Show(cursor.CurrentWhiteboard);
        yield return PlayPages(LessonSpeechPlan.Build(
            cursor.CurrentLines,
            0,
            LessonSpeaker.Teacher,
            SplitText));

        cursor.EndReplay();
        whiteboard.Show(cursor.CurrentWhiteboard);
    }

    // ----- AraBOT Question and Comprehension Checks ----------------------

    private IEnumerator RunAraBotQuestion(FleeAraBotQuestion question)
    {
        phase = LearningPhaseState.AraBotQuestion;
        cancelRequested = false;
        ApplyControls(false, false, true, LessonLocalLines.SkipLabel(language));

        yield return Say(LessonSpeaker.AraBot, question.line);
        if (cancelRequested)
        {
            cancelRequested = false;
            yield break;
        }

        yield return CaptureText(LearnerName, LessonLocalLines.AnswerInstructions(language));
        if (captureState != CaptureState.Submitted)
        {
            ClearCapturedText();
            yield break;
        }

        ApplyControls(false, false, false, null);
        EnsureCaptureFlow().ShowProcessing(AraBotName);

        FleeAraBotAnswerResponse reaction = null;
        FleeApiFailure failure = null;
        string answer = capturedText;
        ClearCapturedText();
        yield return apiClient.AnswerAraBot(
            classroomId,
            question.questionId,
            answer,
            value => reaction = value,
            error => failure = error);
        EnsureCaptureFlow().Hide();

        if (reaction == null || failure != null)
        {
            yield return Say(LessonSpeaker.AraBot, LessonLocalLines.AraBotAnswerFailed(language));
            yield break;
        }

        yield return Say(LessonSpeaker.AraBot, reaction.arabotLine);
        if (reaction.HasTeacherLine)
        {
            yield return Say(LessonSpeaker.Teacher, reaction.teacherLine);
        }

        if (reaction.HasReplay)
        {
            yield return ReplayStep(reaction.replayStepId);
        }
    }

    private IEnumerator RunChecks()
    {
        phase = LearningPhaseState.ComprehensionCheck;
        IReadOnlyList<FleeComprehensionCheck> checks = cursor.CurrentChecks;
        for (int index = 0; index < checks.Count; index++)
        {
            FleeComprehensionCheck check = checks[index];
            if (check == null || string.IsNullOrWhiteSpace(check.question))
            {
                continue;
            }

            cancelRequested = false;
            ApplyControls(false, false, true, LessonLocalLines.SkipLabel(language));

            List<LessonSpeechPage> asking = new List<LessonSpeechPage>();
            if (index == 0)
            {
                LessonSpeechPlan.Append(
                    asking,
                    LessonLocalLines.CheckIntro(language),
                    LessonSpeaker.Teacher,
                    0,
                    SplitText);
            }

            LessonSpeechPlan.Append(asking, check.question, LessonSpeaker.Teacher, 0, SplitText);
            yield return PlayPages(asking);
            if (cancelRequested)
            {
                cancelRequested = false;
                continue;
            }

            yield return CaptureText(LearnerName, LessonLocalLines.AnswerInstructions(language));
            if (captureState != CaptureState.Submitted)
            {
                ClearCapturedText();
                continue;
            }

            ApplyControls(false, false, false, null);
            EnsureCaptureFlow().ShowProcessing(TeacherName);

            FleeCheckAnswerResponse feedback = null;
            FleeApiFailure failure = null;
            string answer = capturedText;
            ClearCapturedText();
            yield return apiClient.AnswerCheck(
                classroomId,
                check.checkId,
                answer,
                value => feedback = value,
                error => failure = error);
                EnsureCaptureFlow().Hide();

            if (feedback == null || failure != null)
            {
                yield return Say(LessonSpeaker.Teacher, LessonLocalLines.CheckFailed(language));
                continue;
            }

            List<LessonSpeechPage> answering = new List<LessonSpeechPage>();
            LessonSpeechPlan.Append(answering, feedback.teacherLine, LessonSpeaker.Teacher, 0, SplitText);
            LessonSpeechPlan.Append(answering, feedback.modelAnswer, LessonSpeaker.Teacher, 1, SplitText);
            yield return PlayPages(answering);
        }
    }

    // ----- Speech capture ------------------------------------------------

    /// <summary>
    /// Ask her for words through the existing capture flow. The mic button is
    /// relabelled Cancel (or Skip) for as long as it runs, and Escape does the
    /// same, so she can always back out. Ends with <see cref="captureState"/> at
    /// Submitted, with her words in <see cref="capturedText"/>, or at Cancelled.
    /// </summary>
    private IEnumerator CaptureText(string title, string instructions)
    {
        ClearCapturedText();
        cancelRequested = false;
        captureState = CaptureState.Waiting;
        ApplyControls(false, false, true, phase == LearningPhaseState.Interjection
            ? LessonLocalLines.CancelLabel(language)
            : LessonLocalLines.SkipLabel(language));

        DialogueSpeechCaptureFlow flow = EnsureCaptureFlow();
        flow.Show(title, instructions, HandleCaptured);

        while (captureState == CaptureState.Waiting)
        {
            if (cancelRequested || WasEscapePressed())
            {
                cancelRequested = false;
                captureState = CaptureState.Cancelled;
                flow.Hide();
                break;
            }

            yield return null;
        }

        ApplyControls(false, false, false, null);
    }

    private void HandleCaptured(string transcript)
    {
        if (captureState != CaptureState.Waiting)
        {
            return;
        }

        // The box passes a stock sentence through when she confirms with
        // nothing heard. That is no answer, so it counts as skipping.
        capturedText = LessonAnswerText.Clean(transcript);
        captureState = capturedText.Length > 0 ? CaptureState.Submitted : CaptureState.Cancelled;
    }

    private void ClearCapturedText()
    {
        capturedText = string.Empty;
        if (captureState != CaptureState.Waiting)
        {
            captureState = CaptureState.Idle;
        }
    }

    private DialogueSpeechCaptureFlow EnsureCaptureFlow()
    {
        if (captureFlow == null)
        {
            captureFlow = DialogueSpeechCaptureFlow.GetOrCreate();
        }

        return captureFlow;
    }

    private static bool WasEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            return true;
        }
#endif

        return false;
    }

    // ----- Dialogue ------------------------------------------------------

    private IEnumerator Say(LessonSpeaker speaker, string text)
    {
        yield return PlayPages(LessonSpeechPlan.BuildSingle(text, speaker, SplitText));
    }

    /// <summary>
    /// Play pages as one dialogue and wait until it ends, whether she clicked
    /// through the last page or a button stopped it. Which one it was is told
    /// by <see cref="intent"/> and <see cref="cancelRequested"/>.
    /// </summary>
    private IEnumerator PlayPages(List<LessonSpeechPage> pages)
    {
        if (pages == null || pages.Count == 0 || dialogueManager == null)
        {
            yield break;
        }

        List<IDialogueLine> lines = new List<IDialogueLine>(pages.Count);
        for (int index = 0; index < pages.Count; index++)
        {
            LessonSpeechPage page = pages[index];
            lines.Add(new RuntimeDialogueLine(
                ActorFor(page.Speaker),
                page.Speaker == LessonSpeaker.AraBot ? AraBotName : TeacherName,
                page.Text));
        }

        sequenceCounter++;
        RuntimeDialogueSequence sequence = new RuntimeDialogueSequence(
            "learning-" + sequenceCounter,
            lines);
        activePages = pages;
        if (!dialogueManager.Play(sequence))
        {
            activePages = null;
            yield break;
        }

        while (dialogueManager != null
            && dialogueManager.IsPlaying
            && ReferenceEquals(dialogueManager.ActiveDialogue, sequence))
        {
            yield return null;
        }

        activePages = null;
    }

    private UnityEngine.Object ActorFor(LessonSpeaker speaker)
    {
        return speaker == LessonSpeaker.AraBot ? (UnityEngine.Object)araBotActor : teacherActor;
    }

    /// <summary>
    /// The dialogue box reports which page it is on. The cursor wants the line
    /// that page belongs to, so a Learner Interjection can save the right place.
    /// </summary>
    private void HandleLineChanged(IDialogueLine line, int pageIndex)
    {
        if (!trackLines || cursor == null)
        {
            return;
        }

        int sourceLine = LessonSpeechPlan.SourceLineOf(activePages, pageIndex);
        if (sourceLine >= 0)
        {
            cursor.SetLine(sourceLine);
        }
    }

    private static IReadOnlyList<string> SplitText(string text)
    {
        return DialogueTextPaginator.Split(text);
    }

    // ----- Buttons -------------------------------------------------------

    private void HandleContinueClicked()
    {
        if (phase == LearningPhaseState.AwaitingContinue)
        {
            intent = PendingIntent.Continue;
        }
    }

    private void HandleBackClicked()
    {
        bool canMove = phase == LearningPhaseState.Teaching
            || phase == LearningPhaseState.AwaitingContinue;
        if (canMove && cursor != null && cursor.CanGoBack)
        {
            intent = PendingIntent.Back;
            StopDialogue();
        }
    }

    private void HandleMicClicked()
    {
        switch (phase)
        {
            case LearningPhaseState.Teaching:
            case LearningPhaseState.AwaitingContinue:
                if (micAvailable)
                {
                    intent = PendingIntent.Mic;
                    StopDialogue();
                }

                break;

            case LearningPhaseState.AraBotQuestion:
            case LearningPhaseState.ComprehensionCheck:
            case LearningPhaseState.Interjection:
                // While she is being asked something the mic button is Cancel or Skip.
                cancelRequested = true;
                StopDialogue();
                break;
        }
    }

    private void StopDialogue()
    {
        if (dialogueManager != null && dialogueManager.IsPlaying)
        {
            dialogueManager.EndDialogue();
        }
    }

    private void ApplyControls(bool continueVisible, bool backAvailable, bool micVisible, string micLabel)
    {
        if (controls == null)
        {
            return;
        }

        controls.SetContinueVisible(continueVisible);
        controls.SetBackAvailable(backAvailable);
        controls.SetMicLabel(micLabel);
        controls.SetMicAvailable(micVisible);
    }

    // ----- Scene ---------------------------------------------------------

    private void ResolveActors()
    {
        if (teacherActor == null)
        {
            teacherActor = GetComponent<DialogueActor>();
        }

        DialogueActor[] actors = FindAll<DialogueActor>(true);
        for (int index = 0; index < actors.Length; index++)
        {
            DialogueActor actor = actors[index];
            if (actor == null)
            {
                continue;
            }

            if (teacherActor == null
                && string.Equals(actor.DisplayName, TeacherName, StringComparison.OrdinalIgnoreCase))
            {
                teacherActor = actor;
            }

            if (araBotActor == null
                && string.Equals(actor.DisplayName, AraBotName, StringComparison.OrdinalIgnoreCase))
            {
                araBotActor = actor;
            }
        }
    }

    /// <summary>
    /// Nobody walks or talks to a Pupil in Learning. AraBOT stays where she is
    /// (locked rather than hidden, because the capture flow hangs its mic
    /// prompt on her), and any Pupil there is stops being interactive.
    /// </summary>
    private void ConfigureScene()
    {
        AraBotClickToMove movement = FindFirst<AraBotClickToMove>(true);
        if (movement != null)
        {
            movement.SetConversationMovementLocked(true);
        }

        DisableStudentInteractions();

        ClassroomSessionController session = FindFirst<ClassroomSessionController>(true);
        if (session != null)
        {
            session.SetConversationCounterVisible(false);
        }
    }

    private void DisableStudentInteractions()
    {
        StudentDialogueInteraction[] interactions = FindAll<StudentDialogueInteraction>(true);
        for (int index = 0; index < interactions.Length; index++)
        {
            interactions[index].enabled = false;
        }

        StudentRoamingController[] roaming = FindAll<StudentRoamingController>(true);
        for (int index = 0; index < roaming.Length; index++)
        {
            roaming[index].enabled = false;
        }
    }

    private IEnumerator ObtainClassroom(Action<FleeClassroomSession> done)
    {
        if (buildClassroomHere)
        {
            FleeClassroomSession built = null;
            FleeApiFailure failure = null;
            yield return apiClient.PrepareClassroom(
                value => built = value,
                error => failure = error,
                (value, message) => UpdateLoadingTask(value * 0.75f, message));
            if (failure != null)
            {
                Debug.LogWarning("Learning could not prepare the Classroom: " + failure.Message, this);
            }

            done(built);
            yield break;
        }

        // The Classroom belongs to a ClassroomSessionController in the scene:
        // wait for it, then take its Pupils out of play.
        float deadline = Time.realtimeSinceStartup + classroomLoadTimeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (classroomSession == null)
            {
                classroomSession = FindFirst<ClassroomSessionController>(false);
            }

            if (classroomSession != null
                && classroomSession.Classroom != null
                && classroomSession.SpawnedStudentCount > 0)
            {
                break;
            }

            yield return null;
        }

        DisableStudentInteractions();
        FleeClassroomSession waited = classroomSession != null ? classroomSession.Classroom : null;
        if (waited == null && FleeApiClient.HasActiveClassroom)
        {
            waited = apiClient.ActiveClassroom;
        }

        done(waited);
    }

    private void BuildInterface()
    {
        // The Exit button of the dialogue box is the look every other button in
        // the game copies. It is found even while the box is hidden, and hidden
        // itself afterwards: there is no conversation to exit in a Lesson.
        Button styleSource = null;
        TMP_FontAsset font = null;
        DialogueExitConversationButton exitButton = FindFirst<DialogueExitConversationButton>(true);
        if (exitButton != null)
        {
            styleSource = exitButton.GetComponent<Button>();
            TMP_Text sourceLabel = exitButton.GetComponentInChildren<TMP_Text>(true);
            font = sourceLabel != null ? sourceLabel.font : null;
        }

        GameObject canvasObject = new GameObject(
            "Learning Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.layer = 5;
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = canvasReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;

        RectTransform canvasRect = canvasObject.transform as RectTransform;

        whiteboard = LessonWhiteboardView.Create(canvasRect, font);
        whiteboard.Rect.sizeDelta = whiteboardSize;
        whiteboard.Rect.anchoredPosition = whiteboardPosition;
        whiteboard.SetVisible(false);

        controls = LessonControlsView.Create(canvasRect, font, styleSource);
        controls.ContinueClicked += HandleContinueClicked;
        controls.BackClicked += HandleBackClicked;
        controls.MicClicked += HandleMicClicked;

        if (exitButton != null)
        {
            exitButton.gameObject.SetActive(false);
        }
    }

    private void LeaveToClassroom()
    {
        if (isLeaving)
        {
            return;
        }

        isLeaving = true;
        phase = LearningPhaseState.Leaving;
        CompleteLoadingTask("Heading to the Classroom.");
        StopDialogue();
        if (captureState == CaptureState.Waiting)
        {
            EnsureCaptureFlow().Hide();
        }

        if (controls != null)
        {
            controls.SetVisible(false);
        }

        DoorSceneTransition.LoadScene(ClassroomSceneName, ClassroomScenePath);
    }

    private void UpdateLoadingTask(float value, string message)
    {
        if (isLoadingTaskActive)
        {
            DoorSceneTransition.UpdateLoadingTask(LoadingTaskId, Mathf.Clamp01(value), message);
        }
    }

    private void CompleteLoadingTask(string message)
    {
        if (!isLoadingTaskActive)
        {
            return;
        }

        DoorSceneTransition.CompleteLoadingTask(LoadingTaskId, message);
        isLoadingTaskActive = false;
    }

    /// <summary>
    /// Run a coroutine and, if it throws, log it and stop instead of leaving the
    /// scene with nothing running. Unity ends a coroutine that throws without
    /// telling its caller, so the nested coroutines are stepped by hand here to
    /// be able to catch them. Anything that is not a nested coroutine goes back
    /// to Unity to wait on as usual.
    /// </summary>
    private IEnumerator RunGuarded(IEnumerator root)
    {
        Stack<IEnumerator> stack = new Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            bool moved = false;
            bool faulted = false;
            object current = null;
            try
            {
                moved = top.MoveNext();
                if (moved)
                {
                    current = top.Current;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                faulted = true;
            }

            if (faulted)
            {
                yield break;
            }

            if (!moved)
            {
                stack.Pop();
                continue;
            }

            IEnumerator nested = current as IEnumerator;
            if (nested != null)
            {
                stack.Push(nested);
                continue;
            }

            yield return current;
        }
    }

    private static T FindFirst<T>(bool includeInactive) where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<T>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
        return FindObjectOfType<T>(includeInactive);
#endif
    }

    private static T[] FindAll<T>(bool includeInactive) where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        return FindObjectsByType<T>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
#else
        return FindObjectsOfType<T>(includeInactive);
#endif
    }
}

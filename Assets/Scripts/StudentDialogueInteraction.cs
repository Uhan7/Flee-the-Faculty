using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class StudentDialogueInteraction : MonoBehaviour
{
    public static event System.Action<DialogueActor> ConversationStarted;
    public static event System.Action AraBotResponseRequested;
    public static event System.Action ConversationEnded;
    public static event System.Action<DialogueActor> ConversationCompleted;

    [SerializeField] private SceneDialogueConversation dialogue;
    [SerializeField] private Transform activator;
    [SerializeField] private Button questionButton;
    [SerializeField] private GameObject questionButtonRoot;

    [Header("Restart Button")]
    [SerializeField] private string restartButtonText = "RESTART";
    [SerializeField] private Vector2 restartButtonSize = new Vector2(2.4f, 0.8f);

    [Header("Speech Reply Flow")]
    [SerializeField] private bool useSpeechReplyFlow = true;
    [SerializeField] private DialogueActor studentActor;
    [SerializeField] private string fallbackQuestionText = "Hey, AraBOT. What should I say to the teacher?";
    [SerializeField] private string speechPromptTitle = "AraBOT";
    [SerializeField, TextArea(2, 4)] private string speechPromptInstructions = "Tap the mic above AraBOT, speak your answer, then continue when you are done.";
    [SerializeField] private string repeatLineFormat = "Okay, so you said: \"{0}\"";
    [SerializeField] private string emptyTranscriptFallback = "I did not catch that.";

    [Header("Backend API Test")]
    [SerializeField] private bool useBackendReplyFlow;
    [SerializeField, HideInInspector] private string backendPupilId;
    [SerializeField] private string backendPupilName = "Mary";

    private Coroutine beginDialogueRoutine;
    private Collider2D interactionZone;
    private DialogueManager dialogueManager;
    private StudentRoamingController roamingController;
    private AraBotClickToMove activeActivatorMovement;
    private Transform activeActivatorTransform;
    private CharacterActivityBubble activityBubble;
    private DialogueSpeechCaptureFlow speechCaptureFlow;
    private RuntimeDialogueSequence activeQuestionDialogue;
    private RuntimeDialogueSequence activeRepeatDialogue;
    private RuntimeDialogueSequence preloadedQuestionDialogue;
    private FleeApiClient apiClient;
    private FleeEncounterSession activeEncounter;
    private FleeApiFailure backendPreloadFailure;
    private bool hasCompletedDialogue;
    private bool hasReportedConversationCompleted;
    private bool isActivatorInside;
    private bool isBackendQuestionLoading;
    private bool isBackendQuestionReady;
    private bool isConversationModeActive;
    private bool isSpeechFlowActive;
    private bool isRestartAvailable;
    private string lastCapturedSpeech = string.Empty;
    private TMP_Text restartButtonLabel;
    private RectTransform questionButtonRect;
    private Vector2 questionButtonOriginalSize;

    private static StudentDialogueInteraction activeConversation;

    public string LastCapturedSpeech => lastCapturedSpeech;

    public static void ExitActiveConversation()
    {
        if (activeConversation != null)
        {
            activeConversation.ExitConversation();
        }
    }

    public static void SkipActiveConversation()
    {
        if (activeConversation != null)
        {
            activeConversation.SkipConversation();
        }
    }

    public void ExitConversation()
    {
        if (!isConversationModeActive)
        {
            return;
        }

        if (beginDialogueRoutine != null)
        {
            StopCoroutine(beginDialogueRoutine);
            beginDialogueRoutine = null;
        }

        if (isBackendQuestionLoading)
        {
            isBackendQuestionLoading = false;
            DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), "Conversation cancelled.");
        }

        isSpeechFlowActive = false;
        activeQuestionDialogue = null;
        activeRepeatDialogue = null;
        preloadedQuestionDialogue = null;
        activeEncounter = null;
        backendPreloadFailure = null;
        isBackendQuestionReady = false;
        SetThinkingBubbleVisible(false);

        if (speechCaptureFlow != null)
        {
            speechCaptureFlow.Hide();
        }

        if (dialogueManager != null && dialogueManager.IsPlaying)
        {
            dialogueManager.EndDialogue();
        }

        EndConversationMode();
        ClearInteractionTargetFocus();
        RefreshQuestionButton();
    }

    private void SkipConversation()
    {
        if (!isConversationModeActive)
        {
            return;
        }

        if (beginDialogueRoutine != null)
        {
            StopCoroutine(beginDialogueRoutine);
            beginDialogueRoutine = null;
        }

        if (isBackendQuestionLoading)
        {
            isBackendQuestionLoading = false;
            DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), "Conversation skipped.");
        }

        // Clear dialogue state before ending the manager so its end event cannot advance the flow.
        hasCompletedDialogue = true;
        isSpeechFlowActive = false;
        activeQuestionDialogue = null;
        activeRepeatDialogue = null;

        if (speechCaptureFlow != null)
        {
            speechCaptureFlow.Hide();
        }

        if (dialogueManager != null && dialogueManager.IsPlaying)
        {
            dialogueManager.EndDialogue();
        }

        CompleteInteraction();
    }

    public void ConfigureBackendPupil(FleePupilSession pupil)
    {
        if (pupil == null)
        {
            return;
        }

        useSpeechReplyFlow = true;
        useBackendReplyFlow = true;
        backendPupilId = pupil.PupilId;
        backendPupilName = pupil.Name;

        // GDD 8.3. The prefab carries a base voice; the Classroom carries which
        // of the six slots this Character speaks in, and that is the one the
        // service renders. Without this line every girl in the Classroom asks
        // for the same voice and three of them sound like one person.
        DialogueActor actor = ResolveStudentActor();
        if (actor != null)
        {
            actor.SetVoiceSlot(pupil.Voice);
        }
        activeEncounter = null;
        isBackendQuestionReady = false;
        backendPreloadFailure = null;
        isRestartAvailable = false;
        preloadedQuestionDialogue = BuildQuestionDialogue();
    }

    private void Reset()
    {
        interactionZone = GetComponent<Collider2D>();
        if (interactionZone != null)
        {
            interactionZone.isTrigger = true;
        }

        if (questionButton == null)
        {
            questionButton = GetComponentInChildren<Button>(true);
        }

        if (questionButtonRoot == null && questionButton != null)
        {
            questionButtonRoot = questionButton.gameObject;
        }

        if (studentActor == null)
        {
            studentActor = GetComponentInParent<DialogueActor>();
        }
    }

    private void Awake()
    {
        interactionZone = GetComponent<Collider2D>();
        interactionZone.isTrigger = true;
        roamingController = GetComponentInParent<StudentRoamingController>();
        studentActor = ResolveStudentActor();
        activityBubble = studentActor != null ? studentActor.GetComponent<CharacterActivityBubble>() : null;
        preloadedQuestionDialogue = BuildQuestionDialogue();

        if (questionButtonRoot == null && questionButton != null)
        {
            questionButtonRoot = questionButton.gameObject;
        }

        CacheQuestionButtonPresentation();

        SetQuestionButtonVisible(false);
    }

    private void OnEnable()
    {
        if (questionButton != null)
        {
            questionButton.onClick.AddListener(OnQuestionButtonPressed);
        }

        dialogueManager = DialogueManager.GetOrCreate();
        if (dialogueManager != null)
        {
            dialogueManager.DialogueStarted += HandleDialogueStarted;
            dialogueManager.DialogueEnded += HandleDialogueEnded;
        }

        ConversationStarted += HandleConversationStarted;
        ConversationEnded += HandleConversationEnded;

        preloadedQuestionDialogue = BuildQuestionDialogue();
        RefreshQuestionButton();
    }

    private void OnDisable()
    {
        if (questionButton != null)
        {
            questionButton.onClick.RemoveListener(OnQuestionButtonPressed);
        }

        if (dialogueManager != null)
        {
            dialogueManager.DialogueStarted -= HandleDialogueStarted;
            dialogueManager.DialogueEnded -= HandleDialogueEnded;
        }

        ConversationStarted -= HandleConversationStarted;
        ConversationEnded -= HandleConversationEnded;

        if (beginDialogueRoutine != null)
        {
            StopCoroutine(beginDialogueRoutine);
            beginDialogueRoutine = null;
            if (isBackendQuestionLoading)
            {
                isBackendQuestionLoading = false;
                DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), "Loading skipped.");
            }
        }

        if (speechCaptureFlow != null)
        {
            speechCaptureFlow.Hide();
        }

        EndConversationMode();
        SetThinkingBubbleVisible(false);

        ClearInteractionTargetFocus();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!CanBeActivatedBy(other) || hasCompletedDialogue)
        {
            return;
        }

        isActivatorInside = true;
        ApplyInteractionTargetFocus(other);
        RefreshQuestionButton();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!CanBeActivatedBy(other))
        {
            return;
        }

        isActivatorInside = false;
        if (!isConversationModeActive)
        {
            ClearInteractionTargetFocus(other);
        }

        RefreshQuestionButton();
    }

    public void OnQuestionButtonPressed()
    {
        if (!CanStartInteraction())
        {
            return;
        }

        if (isRestartAvailable)
        {
            PrepareRestart();
        }

        SetQuestionButtonVisible(false);
        BeginConversationMode();

        if (beginDialogueRoutine != null)
        {
            StopCoroutine(beginDialogueRoutine);
        }

        beginDialogueRoutine = StartCoroutine(useSpeechReplyFlow
            ? BeginSpeechReplyFlowNextFrame()
            : BeginDialogueNextFrame());
    }

    private IEnumerator BeginDialogueNextFrame()
    {
        yield return null;
        beginDialogueRoutine = null;

        if (!CanStartDefaultDialogue())
        {
            EndConversationMode();
            RefreshQuestionButton();
            yield break;
        }

        if (dialogueManager == null)
        {
            dialogueManager = DialogueManager.GetOrCreate();
        }

        if (dialogueManager == null || !dialogueManager.Play(dialogue))
        {
            EndConversationMode();
            RefreshQuestionButton();
        }
    }

    private IEnumerator BeginSpeechReplyFlowNextFrame()
    {
        yield return null;

        if (!CanStartSpeechReplyFlow())
        {
            beginDialogueRoutine = null;
            EndConversationMode();
            RefreshQuestionButton();
            yield break;
        }

        if (dialogueManager == null)
        {
            dialogueManager = DialogueManager.GetOrCreate();
        }

        if (useBackendReplyFlow && !isBackendQuestionReady)
        {
            SetThinkingBubbleVisible(true);
            yield return PreloadBackendQuestion();
            SetThinkingBubbleVisible(false);
            if (!isBackendQuestionReady)
            {
                beginDialogueRoutine = null;
                EndConversationMode();
                RefreshQuestionButton();
                yield break;
            }
        }

        activeQuestionDialogue = preloadedQuestionDialogue ?? BuildQuestionDialogue();
        if (dialogueManager == null || activeQuestionDialogue == null || !activeQuestionDialogue.HasLines)
        {
            beginDialogueRoutine = null;
            EndConversationMode();
            RefreshQuestionButton();
            yield break;
        }

        isSpeechFlowActive = true;
        lastCapturedSpeech = string.Empty;
        beginDialogueRoutine = null;
        if (!dialogueManager.Play(activeQuestionDialogue))
        {
            isSpeechFlowActive = false;
            activeQuestionDialogue = null;
            EndConversationMode();
            RefreshQuestionButton();
        }
    }

    private void HandleDialogueEnded(IDialogueSequence finishedDialogue)
    {
        if (useSpeechReplyFlow && isSpeechFlowActive)
        {
            if (ReferenceEquals(finishedDialogue, activeQuestionDialogue))
            {
                activeQuestionDialogue = null;
                if (useBackendReplyFlow
                    && activeEncounter != null
                    && !activeEncounter.CanAcceptExplanation)
                {
                    CompleteInteraction();
                    return;
                }

                ShowSpeechCaptureFlow();
                return;
            }

            if (ReferenceEquals(finishedDialogue, activeRepeatDialogue))
            {
                activeRepeatDialogue = null;
                CompleteInteraction();
                return;
            }
        }

        if (!useSpeechReplyFlow && ReferenceEquals(finishedDialogue, dialogue))
        {
            CompleteInteraction();
            return;
        }

        RefreshQuestionButton();
    }

    private void HandleDialogueStarted(IDialogueSequence _)
    {
        SetQuestionButtonVisible(false);
    }

    private void HandleConversationStarted(DialogueActor _)
    {
        if (activeConversation != this)
        {
            SetQuestionButtonVisible(false);
        }
    }

    private void HandleConversationEnded()
    {
        RefreshQuestionButton();
    }

    private void ShowSpeechCaptureFlow()
    {
        speechCaptureFlow = DialogueSpeechCaptureFlow.GetOrCreate();
        if (speechCaptureFlow == null)
        {
            CompleteInteraction();
            return;
        }

        AraBotResponseRequested?.Invoke();
        speechCaptureFlow.Show(speechPromptTitle, speechPromptInstructions, HandleSpeechCaptured);
    }

    private void HandleSpeechCaptured(string transcript)
    {
        lastCapturedSpeech = NormalizeCapturedSpeech(transcript);

        if (useBackendReplyFlow)
        {
            if (activeEncounter == null)
            {
                ShowBackendFailure(backendPreloadFailure);
                return;
            }

            speechCaptureFlow.ShowProcessing(ResolveStudentSpeakerName());
            SetThinkingBubbleVisible(true);
            beginDialogueRoutine = StartCoroutine(SubmitBackendReply(transcript));
            return;
        }

        activeRepeatDialogue = BuildRepeatDialogue(lastCapturedSpeech);

        if (dialogueManager == null)
        {
            dialogueManager = DialogueManager.GetOrCreate();
        }

        if (dialogueManager == null || activeRepeatDialogue == null || !dialogueManager.Play(activeRepeatDialogue))
        {
            CompleteInteraction();
        }
    }

    private IEnumerator PreloadBackendQuestion()
    {
        isBackendQuestionLoading = true;
        isBackendQuestionReady = false;
        backendPreloadFailure = null;
        DoorSceneTransition.TryRegisterLoadingTask(
            GetBackendLoadingTaskId(),
            "Generating " + backendPupilName + "'s question...",
            0f);
        apiClient = FleeApiClient.GetOrCreate();

        FleeEncounterSession encounter = null;
        FleeApiFailure failure = null;
        yield return apiClient.BeginEncounter(
            backendPupilId,
            backendPupilName,
            value => encounter = value,
            error => failure = error,
            (progress, status) => DoorSceneTransition.UpdateLoadingTask(
                GetBackendLoadingTaskId(),
                progress,
                status));

        isBackendQuestionLoading = false;
        if (!isActiveAndEnabled || hasCompletedDialogue)
        {
            DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), "Loading skipped.");
            yield break;
        }

        backendPreloadFailure = failure;
        activeEncounter = encounter;
        preloadedQuestionDialogue = failure == null && activeEncounter != null
            ? BuildQuestionDialogue(activeEncounter.OpeningLine)
            : BuildQuestionDialogue();
        isBackendQuestionReady = preloadedQuestionDialogue != null && preloadedQuestionDialogue.HasLines;
        if (failure == null && activeEncounter != null && isBackendQuestionReady && preloadedQuestionDialogue != null && preloadedQuestionDialogue.HasLines)
        {
            DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), backendPupilName + " is ready.");
        }
        else
        {
            DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), "Question ready.");
        }

        RefreshQuestionButton();
    }

    private IEnumerator SubmitBackendReply(string transcript)
    {
        FleeTurnResult result = null;
        FleeApiFailure failure = null;

        yield return apiClient.SubmitTurn(
            activeEncounter,
            transcript,
            turn => result = turn,
            error => failure = error);

        beginDialogueRoutine = null;
        SetThinkingBubbleVisible(false);
        if (!isActiveAndEnabled || !isSpeechFlowActive)
        {
            yield break;
        }

        if (speechCaptureFlow != null)
        {
            speechCaptureFlow.Hide();
        }

        if (failure != null || result == null)
        {
            ShowBackendFailure(failure);
            yield break;
        }

        activeRepeatDialogue = BuildBackendReplyDialogue(result);
        if (dialogueManager == null || activeRepeatDialogue == null || !dialogueManager.Play(activeRepeatDialogue))
        {
            CompleteInteraction();
        }
    }

    private void ShowBackendFailure(FleeApiFailure failure)
    {
        SetThinkingBubbleVisible(false);
        string line = failure != null
            ? failure.ToDialogueLine()
            : "I couldn't reach the science classroom just now. Can we try again in a moment?";
        activeRepeatDialogue = BuildStudentDialogue("api-student-error", new[] { line });

        if (dialogueManager == null)
        {
            dialogueManager = DialogueManager.GetOrCreate();
        }

        if (dialogueManager == null || activeRepeatDialogue == null || !dialogueManager.Play(activeRepeatDialogue))
        {
            CompleteInteraction();
        }
    }

    private RuntimeDialogueSequence BuildQuestionDialogue(string questionOverride = null)
    {
        string questionText = string.IsNullOrWhiteSpace(questionOverride)
            ? fallbackQuestionText
            : questionOverride.Trim();
        Object speakerReference = ResolveStudentSpeakerReference();
        string speakerName = ResolveStudentSpeakerName();

        if (string.IsNullOrWhiteSpace(questionOverride) && dialogue != null && dialogue.HasLines)
        {
            for (int index = 0; index < dialogue.Lines.Count; index++)
            {
                SceneDialogueLine line = dialogue.Lines[index];
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                {
                    continue;
                }

                questionText = line.Text.Trim();

                if (line.SpeakerReference != null)
                {
                    speakerReference = line.SpeakerReference;
                }

                if (!string.IsNullOrWhiteSpace(line.SpeakerName))
                {
                    speakerName = line.SpeakerName;
                }

                break;
            }
        }

        if (string.IsNullOrWhiteSpace(questionText))
        {
            return null;
        }

        return BuildDialogueForSpeaker(
            "speech-student-question",
            speakerReference,
            speakerName,
            new[] { questionText });
    }

    private RuntimeDialogueSequence BuildRepeatDialogue(string transcript)
    {
        string normalizedTranscript = NormalizeCapturedSpeech(transcript);
        string safeRepeatFormat = string.IsNullOrWhiteSpace(repeatLineFormat)
            ? "Okay, so you said: \"{0}\""
            : repeatLineFormat;

        string repeatText;
        try
        {
            repeatText = string.Format(safeRepeatFormat, normalizedTranscript);
        }
        catch
        {
            repeatText = safeRepeatFormat + " " + normalizedTranscript;
        }

        return BuildStudentDialogue("speech-student-repeat", new[] { repeatText });
    }

    private RuntimeDialogueSequence BuildBackendReplyDialogue(FleeTurnResult result)
    {
        if (result == null)
        {
            return null;
        }

        if (result.EncounterEnded)
        {
            return string.IsNullOrWhiteSpace(result.ClosingLine)
                ? BuildStudentDialogue("api-student-reply", new[] { result.Restatement })
                : BuildStudentDialogue(
                    "api-student-reply",
                    new[] { result.Restatement, result.ClosingLine });
        }

        if (string.IsNullOrWhiteSpace(result.FollowUp))
        {
            return BuildStudentDialogue("api-student-reply", new[] { result.Restatement });
        }

        return BuildStudentDialogue(
            "api-student-reply",
            new[] { result.Restatement, result.FollowUp });
    }

    private RuntimeDialogueSequence BuildStudentDialogue(string conversationId, string[] lines)
    {
        return BuildDialogueForSpeaker(
            conversationId,
            ResolveStudentSpeakerReference(),
            ResolveStudentSpeakerName(),
            lines);
    }

    private static RuntimeDialogueSequence BuildDialogueForSpeaker(
        string conversationId,
        Object speakerReference,
        string speakerName,
        string[] lines)
    {
        if (lines == null || lines.Length == 0)
        {
            return null;
        }

        List<RuntimeDialogueLine> dialogueLines = new List<RuntimeDialogueLine>();
        for (int index = 0; index < lines.Length; index++)
        {
            IReadOnlyList<string> pages = DialogueTextPaginator.Split(lines[index]);
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                dialogueLines.Add(new RuntimeDialogueLine(
                    speakerReference,
                    speakerName,
                    pages[pageIndex]));
            }
        }

        return dialogueLines.Count > 0
            ? new RuntimeDialogueSequence(conversationId, dialogueLines)
            : null;
    }

    private string NormalizeCapturedSpeech(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return emptyTranscriptFallback;
        }

        string normalizedTranscript = transcript.Trim();
        if (normalizedTranscript == "No speech or fallback text was submitted.")
        {
            return emptyTranscriptFallback;
        }

        return normalizedTranscript;
    }

    private void SetThinkingBubbleVisible(bool visible)
    {
        if (activityBubble == null && studentActor != null)
        {
            activityBubble = studentActor.GetComponent<CharacterActivityBubble>();
        }

        if (activityBubble != null)
        {
            activityBubble.SetThinking(visible);
        }
    }

    private bool CanStartInteraction()
    {
        return !HasAnotherActiveConversation()
            && (useSpeechReplyFlow ? CanStartSpeechReplyFlow() : CanStartDefaultDialogue());
    }

    private bool CanStartDefaultDialogue()
    {
        return dialogue != null
            && !hasCompletedDialogue
            && !isSpeechFlowActive
            && isActivatorInside
            && dialogueManager != null
            && !dialogueManager.IsPlaying;
    }

    private bool CanStartSpeechReplyFlow()
    {
        return !hasCompletedDialogue
            && !isSpeechFlowActive
            && isActivatorInside
            && dialogueManager != null
            && !dialogueManager.IsPlaying
            && (dialogue == null || dialogue.HasLines || !string.IsNullOrWhiteSpace(fallbackQuestionText));
    }

    private bool CanBeActivatedBy(Collider2D other)
    {
        if (other == null || other.attachedRigidbody == null)
        {
            return false;
        }

        if (activator == null)
        {
            return ResolveAraBotMovement(other.attachedRigidbody.transform) != null;
        }

        Transform movingRoot = other.attachedRigidbody.transform;
        return movingRoot == activator || movingRoot.IsChildOf(activator) || activator.IsChildOf(movingRoot);
    }

    private void RefreshQuestionButton()
    {
        bool shouldShowButton = !hasCompletedDialogue
            && !isSpeechFlowActive
            && isActivatorInside
            && dialogueManager != null
            && !dialogueManager.IsPlaying
            && !HasAnotherActiveConversation();

        SetQuestionButtonVisible(shouldShowButton);
    }

    private bool HasAnotherActiveConversation()
    {
        return activeConversation != null && activeConversation != this;
    }

    private void SetQuestionButtonVisible(bool visible)
    {
        UpdateQuestionButtonPresentation();

        if (questionButton != null)
        {
            questionButton.interactable = visible;
        }

        if (questionButtonRoot != null)
        {
            questionButtonRoot.SetActive(visible);
        }
    }

    private void CacheQuestionButtonPresentation()
    {
        if (questionButton == null)
        {
            return;
        }

        questionButtonRect = questionButton.transform as RectTransform;
        if (questionButtonRect != null)
        {
            questionButtonOriginalSize = questionButtonRect.sizeDelta;
        }

        restartButtonLabel = questionButton.GetComponentInChildren<TMP_Text>(true);
        if (restartButtonLabel != null)
        {
            return;
        }

        GameObject labelObject = new GameObject(
            "Restart Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = questionButton.gameObject.layer;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(questionButton.transform, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(0.14f, 0.08f);
        labelRect.offsetMax = new Vector2(-0.14f, -0.08f);

        restartButtonLabel = labelObject.GetComponent<TextMeshProUGUI>();
        restartButtonLabel.alignment = TextAlignmentOptions.Center;
        restartButtonLabel.enableAutoSizing = true;
        restartButtonLabel.fontSizeMin = 0.14f;
        restartButtonLabel.fontSizeMax = 0.34f;
        restartButtonLabel.fontStyle = FontStyles.Bold;
        restartButtonLabel.color = new Color(0.2f, 0.12f, 0.08f, 1f);
        restartButtonLabel.raycastTarget = false;
    }

    private void UpdateQuestionButtonPresentation()
    {
        if (questionButton == null)
        {
            return;
        }

        if (questionButtonRect == null || restartButtonLabel == null)
        {
            CacheQuestionButtonPresentation();
        }

        if (questionButtonRect != null)
        {
            questionButtonRect.sizeDelta = isRestartAvailable
                ? restartButtonSize
                : questionButtonOriginalSize;
        }

        if (restartButtonLabel != null)
        {
            restartButtonLabel.text = isRestartAvailable ? restartButtonText : string.Empty;
            restartButtonLabel.gameObject.SetActive(isRestartAvailable);
        }
    }

    private void PrepareRestart()
    {
        isRestartAvailable = false;
        hasCompletedDialogue = false;
        activeEncounter = null;
        preloadedQuestionDialogue = null;
        backendPreloadFailure = null;
        isBackendQuestionReady = false;
        UpdateQuestionButtonPresentation();
    }

    private void ApplyInteractionTargetFocus(Collider2D other)
    {
        if (other == null || other.attachedRigidbody == null)
        {
            return;
        }

        activeActivatorTransform = other.attachedRigidbody.transform;
        activeActivatorMovement = ResolveAraBotMovement(activeActivatorTransform);

        if (roamingController != null)
        {
            roamingController.SetInteractionTarget(activeActivatorTransform);
        }
    }

    private void ClearInteractionTargetFocus(Collider2D other = null)
    {
        if (isConversationModeActive)
        {
            return;
        }

        if (roamingController == null)
        {
            return;
        }

        Transform target = other != null && other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : null;
        roamingController.ClearInteractionTarget(target);
    }

    private void BeginConversationMode()
    {
        bool wasConversationActive = isConversationModeActive;
        isConversationModeActive = true;

        if (activeActivatorMovement == null)
        {
            activeActivatorMovement = ResolveAraBotMovement(activator);
        }

        if (activeActivatorMovement == null)
        {
#if UNITY_2023_1_OR_NEWER
            activeActivatorMovement = FindFirstObjectByType<AraBotClickToMove>();
#else
            activeActivatorMovement = FindObjectOfType<AraBotClickToMove>();
#endif
        }

        if (activeActivatorMovement != null)
        {
            activeActivatorTransform = activeActivatorMovement.transform;
            Collider2D studentMovementCollider = roamingController != null
                ? roamingController.MovementCollider
                : null;
            activeActivatorMovement.SetConversationMovementLocked(true, studentMovementCollider);
        }

        if (roamingController != null && activeActivatorTransform != null)
        {
            roamingController.SetInteractionTarget(activeActivatorTransform);
        }

        if (!wasConversationActive)
        {
            activeConversation = this;
            ConversationStarted?.Invoke(ResolveStudentActor());
        }
    }

    private void EndConversationMode()
    {
        bool wasConversationActive = isConversationModeActive;
        if (activeActivatorMovement != null)
        {
            activeActivatorMovement.SetConversationMovementLocked(false);
        }

        isConversationModeActive = false;
        if (wasConversationActive)
        {
            if (activeConversation == this)
            {
                activeConversation = null;
            }

            ConversationEnded?.Invoke();
        }
    }

    private static AraBotClickToMove ResolveAraBotMovement(Transform candidate)
    {
        if (candidate == null)
        {
            return null;
        }

        AraBotClickToMove movement = candidate.GetComponent<AraBotClickToMove>();
        return movement != null ? movement : candidate.GetComponentInParent<AraBotClickToMove>();
    }

    private DialogueActor ResolveStudentActor()
    {
        if (studentActor != null)
        {
            return studentActor;
        }

        studentActor = GetComponentInParent<DialogueActor>();
        return studentActor;
    }

    private Object ResolveStudentSpeakerReference()
    {
        DialogueActor actor = ResolveStudentActor();
        return actor != null ? actor : this;
    }

    private string ResolveStudentSpeakerName()
    {
        DialogueActor actor = ResolveStudentActor();
        return actor != null ? actor.DisplayName : gameObject.name;
    }

    private void CompleteInteraction()
    {
        bool allowRestart = useBackendReplyFlow;
        hasCompletedDialogue = !allowRestart;
        isRestartAvailable = allowRestart;
        ReportConversationCompleted();

        isSpeechFlowActive = false;
        activeQuestionDialogue = null;
        activeRepeatDialogue = null;
        preloadedQuestionDialogue = null;
        activeEncounter = null;
        isBackendQuestionLoading = false;
        isBackendQuestionReady = false;
        backendPreloadFailure = null;
        SetThinkingBubbleVisible(false);
        EndConversationMode();

        if (speechCaptureFlow != null)
        {
            speechCaptureFlow.Hide();
        }

        ClearInteractionTargetFocus();
        if (allowRestart)
        {
            RefreshQuestionButton();
        }
        else
        {
            SetQuestionButtonVisible(false);
        }
    }

    private string GetBackendLoadingTaskId()
    {
        string pupilKey = string.IsNullOrWhiteSpace(backendPupilId) ? backendPupilName : backendPupilId;
        return gameObject.scene.name + "::pupil-question::" + pupilKey;
    }

    private void ReportConversationCompleted()
    {
        if (hasReportedConversationCompleted)
        {
            return;
        }

        hasReportedConversationCompleted = true;
        ConversationCompleted?.Invoke(ResolveStudentActor());
    }
}

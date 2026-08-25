using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class StudentDialogueInteraction : MonoBehaviour
{
    [SerializeField] private SceneDialogueConversation dialogue;
    [SerializeField] private Transform activator;
    [SerializeField] private Button questionButton;
    [SerializeField] private GameObject questionButtonRoot;

    [Header("Speech Reply Flow")]
    [SerializeField] private bool useSpeechReplyFlow = true;
    [SerializeField] private DialogueActor studentActor;
    [SerializeField] private string fallbackQuestionText = "Hey, AraBOT. What should I say to the teacher?";
    [SerializeField] private string speechPromptTitle = "AraBOT's Reply";
    [SerializeField, TextArea(2, 4)] private string speechPromptInstructions = "Tap the mic above AraBOT, speak your answer, then continue when you are done.";
    [SerializeField] private string repeatLineFormat = "Okay, so you said: \"{0}\"";
    [SerializeField] private string emptyTranscriptFallback = "I did not catch AraBOT's reply.";

    [Header("Backend API Test")]
    [SerializeField] private bool useBackendReplyFlow;
    [SerializeField] private string backendPupilName = "Mary";

    private Coroutine beginDialogueRoutine;
    private Coroutine backendPreloadRoutine;
    private Collider2D interactionZone;
    private DialogueManager dialogueManager;
    private StudentRoamingController roamingController;
    private CharacterActivityBubble activityBubble;
    private DialogueSpeechCaptureFlow speechCaptureFlow;
    private RuntimeDialogueSequence activeQuestionDialogue;
    private RuntimeDialogueSequence activeRepeatDialogue;
    private RuntimeDialogueSequence preloadedQuestionDialogue;
    private FleeApiClient apiClient;
    private FleeEncounterSession activeEncounter;
    private FleeApiFailure backendPreloadFailure;
    private bool hasCompletedDialogue;
    private bool isActivatorInside;
    private bool isBackendQuestionLoading;
    private bool isBackendQuestionReady;
    private bool isSpeechFlowActive;
    private bool retrySpeechAfterReply;
    private string lastCapturedSpeech = string.Empty;

    public string LastCapturedSpeech => lastCapturedSpeech;

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
            dialogueManager.DialogueEnded += HandleDialogueEnded;
        }

        preloadedQuestionDialogue = BuildQuestionDialogue();
        StartBackendQuestionPreloadIfNeeded();
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
            dialogueManager.DialogueEnded -= HandleDialogueEnded;
        }

        if (beginDialogueRoutine != null)
        {
            StopCoroutine(beginDialogueRoutine);
            beginDialogueRoutine = null;
        }

        if (backendPreloadRoutine != null)
        {
            StopCoroutine(backendPreloadRoutine);
            backendPreloadRoutine = null;
            isBackendQuestionLoading = false;
            DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), "Loading skipped.");
        }

        if (speechCaptureFlow != null)
        {
            speechCaptureFlow.Hide();
        }

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
        ClearInteractionTargetFocus(other);
        RefreshQuestionButton();
    }

    public void OnQuestionButtonPressed()
    {
        if (!CanStartInteraction())
        {
            return;
        }

        SetQuestionButtonVisible(false);

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
            RefreshQuestionButton();
            yield break;
        }

        if (dialogueManager == null)
        {
            dialogueManager = DialogueManager.GetOrCreate();
        }

        if (dialogueManager == null || !dialogueManager.Play(dialogue))
        {
            RefreshQuestionButton();
        }
    }

    private IEnumerator BeginSpeechReplyFlowNextFrame()
    {
        yield return null;
        beginDialogueRoutine = null;

        if (!CanStartSpeechReplyFlow())
        {
            RefreshQuestionButton();
            yield break;
        }

        if (dialogueManager == null)
        {
            dialogueManager = DialogueManager.GetOrCreate();
        }

        if (useBackendReplyFlow && !isBackendQuestionReady)
        {
            StartBackendQuestionPreloadIfNeeded();
            RefreshQuestionButton();
            yield break;
        }

        activeQuestionDialogue = preloadedQuestionDialogue ?? BuildQuestionDialogue();
        if (dialogueManager == null || activeQuestionDialogue == null || !activeQuestionDialogue.HasLines)
        {
            RefreshQuestionButton();
            yield break;
        }

        isSpeechFlowActive = true;
        lastCapturedSpeech = string.Empty;

        if (!dialogueManager.Play(activeQuestionDialogue))
        {
            isSpeechFlowActive = false;
            activeQuestionDialogue = null;
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
                ShowSpeechCaptureFlow();
                return;
            }

            if (ReferenceEquals(finishedDialogue, activeRepeatDialogue))
            {
                activeRepeatDialogue = null;
                if (retrySpeechAfterReply)
                {
                    retrySpeechAfterReply = false;
                    ShowSpeechCaptureFlow();
                    return;
                }

                CompleteInteraction();
                return;
            }
        }

        if (!useSpeechReplyFlow && finishedDialogue == dialogue)
        {
            CompleteInteraction();
        }
    }

    private void ShowSpeechCaptureFlow()
    {
        speechCaptureFlow = DialogueSpeechCaptureFlow.GetOrCreate();
        if (speechCaptureFlow == null)
        {
            CompleteInteraction();
            return;
        }

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

            speechCaptureFlow.ShowProcessing(
                "Mary Is Thinking",
                "The AI response can take 15-30 seconds. Please wait and stay in Play mode.");
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

    private void StartBackendQuestionPreloadIfNeeded()
    {
        if (!useSpeechReplyFlow || !useBackendReplyFlow || hasCompletedDialogue || isBackendQuestionReady || isBackendQuestionLoading)
        {
            return;
        }

        if (backendPreloadRoutine != null)
        {
            return;
        }

        backendPreloadRoutine = StartCoroutine(PreloadBackendQuestion());
    }

    private IEnumerator PreloadBackendQuestion()
    {
        isBackendQuestionLoading = true;
        isBackendQuestionReady = false;
        backendPreloadFailure = null;
        DoorSceneTransition.TryRegisterLoadingTask(
            GetBackendLoadingTaskId(),
            "Loading Mary's question...",
            0f);
        apiClient = FleeApiClient.GetOrCreate();

        FleeEncounterSession encounter = null;
        FleeApiFailure failure = null;
        yield return apiClient.BeginEncounter(
            backendPupilName,
            value => encounter = value,
            error => failure = error,
            (progress, status) => DoorSceneTransition.UpdateLoadingTask(
                GetBackendLoadingTaskId(),
                progress,
                status));

        backendPreloadRoutine = null;
        isBackendQuestionLoading = false;
        if (!isActiveAndEnabled || hasCompletedDialogue)
        {
            DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), "Loading skipped.");
            yield break;
        }

        backendPreloadFailure = failure;
        activeEncounter = encounter;
        preloadedQuestionDialogue = failure == null && activeEncounter != null
            ? BuildQuestionDialogue(FormatOpeningAsQuestion(activeEncounter.OpeningLine))
            : BuildQuestionDialogue();
        isBackendQuestionReady = preloadedQuestionDialogue != null && preloadedQuestionDialogue.HasLines;
        if (failure == null && activeEncounter != null && isBackendQuestionReady && preloadedQuestionDialogue != null && preloadedQuestionDialogue.HasLines)
        {
            DoorSceneTransition.CompleteLoadingTask(GetBackendLoadingTaskId(), "Mary is ready.");
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
        retrySpeechAfterReply = activeEncounter != null
            && failure != null
            && (failure.StatusCode == 422
                || failure.StatusCode == 429
                || failure.StatusCode == 502
                || failure.StatusCode == 503);
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

        return new RuntimeDialogueSequence(
            "speech-student-question",
            new[]
            {
                new RuntimeDialogueLine(speakerReference, speakerName, questionText)
            });
    }

    private static string FormatOpeningAsQuestion(string openingLine)
    {
        if (string.IsNullOrWhiteSpace(openingLine))
        {
            return string.Empty;
        }

        string normalizedLine = openingLine.Trim();
        return normalizedLine.EndsWith("?", System.StringComparison.Ordinal)
            ? normalizedLine
            : normalizedLine + " Is that right, AraBOT?";
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

        return new RuntimeDialogueSequence(
            "speech-student-repeat",
            new[]
            {
                new RuntimeDialogueLine(
                    ResolveStudentSpeakerReference(),
                    ResolveStudentSpeakerName(),
                    repeatText)
            });
    }

    private RuntimeDialogueSequence BuildBackendReplyDialogue(FleeTurnResult result)
    {
        if (result == null)
        {
            return null;
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
        if (lines == null || lines.Length == 0)
        {
            return null;
        }

        RuntimeDialogueLine[] dialogueLines = new RuntimeDialogueLine[lines.Length];
        for (int index = 0; index < lines.Length; index++)
        {
            dialogueLines[index] = new RuntimeDialogueLine(
                ResolveStudentSpeakerReference(),
                ResolveStudentSpeakerName(),
                lines[index]);
        }

        return new RuntimeDialogueSequence(conversationId, dialogueLines);
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
        return useSpeechReplyFlow ? CanStartSpeechReplyFlow() : CanStartDefaultDialogue();
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
            && (!useBackendReplyFlow || isBackendQuestionReady)
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
            return true;
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
            && !dialogueManager.IsPlaying;

        if (useBackendReplyFlow)
        {
            shouldShowButton &= isBackendQuestionReady;
        }

        SetQuestionButtonVisible(shouldShowButton);
    }

    private void SetQuestionButtonVisible(bool visible)
    {
        if (questionButton != null)
        {
            questionButton.interactable = visible;
        }

        if (questionButtonRoot != null)
        {
            questionButtonRoot.SetActive(visible);
        }
    }

    private void ApplyInteractionTargetFocus(Collider2D other)
    {
        if (roamingController == null || other == null || other.attachedRigidbody == null)
        {
            return;
        }

        roamingController.SetInteractionTarget(other.attachedRigidbody.transform);
    }

    private void ClearInteractionTargetFocus(Collider2D other = null)
    {
        if (roamingController == null)
        {
            return;
        }

        Transform target = other != null && other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : null;
        roamingController.ClearInteractionTarget(target);
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
        hasCompletedDialogue = true;
        isActivatorInside = false;
        isSpeechFlowActive = false;
        activeQuestionDialogue = null;
        activeRepeatDialogue = null;
        preloadedQuestionDialogue = null;
        activeEncounter = null;
        isBackendQuestionLoading = false;
        isBackendQuestionReady = false;
        retrySpeechAfterReply = false;
        backendPreloadFailure = null;
        SetThinkingBubbleVisible(false);

        if (speechCaptureFlow != null)
        {
            speechCaptureFlow.Hide();
        }

        ClearInteractionTargetFocus();
        SetQuestionButtonVisible(false);
    }

    private string GetBackendLoadingTaskId()
    {
        return gameObject.scene.name + "::mary-question";
    }
}

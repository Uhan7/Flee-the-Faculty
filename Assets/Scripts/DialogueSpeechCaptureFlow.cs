using System;
using TMPro;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class DialogueSpeechCaptureFlow : MonoBehaviour
{
    private const string DefaultWaitingText = "AraBOT is listening";
    private const string NoSpeechSubmittedText = "No speech was submitted.";
    private const string TypeInsteadText = "Click here if you prefer to type.";
    private const string TypedReplyPlaceholder = "Type your reply, then press Enter";

    private static DialogueSpeechCaptureFlow instance;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float inputDebounceSeconds = 0.15f;

    [Header("Prompt Layout")]
    [SerializeField] private Vector3 promptWorldOffset = new Vector3(0f, 2.4f, 0f);
    [SerializeField] private Vector2 promptScreenOffset = new Vector2(0f, 18f);
    [SerializeField] private Vector2 promptScreenSize = new Vector2(112f, 112f);

    private BrowserSpeechToTextPrototype speechController;
    private SceneDialogueView dialogueView;
    private AraBotPromptButton[] promptButtons = Array.Empty<AraBotPromptButton>();
    private CharacterActivityBubble araBotActivityBubble;
    private Transform promptWorldAnchor;
    private Action<string> onTranscriptConfirmed;
    private PromptState state = PromptState.Hidden;
    private string currentTitle = string.Empty;
    private string currentInstructions = string.Empty;
    private string liveTranscript = string.Empty;
    private string pendingTranscript = string.Empty;
    private float ignoreAdvanceUntil;
    private bool hasHeardSpeech;
    private bool isTypingHintVisible;

    public static DialogueSpeechCaptureFlow GetOrCreate()
    {
        if (instance != null)
        {
            return instance;
        }

#if UNITY_2023_1_OR_NEWER
        instance = FindFirstObjectByType<DialogueSpeechCaptureFlow>();
#else
        instance = FindObjectOfType<DialogueSpeechCaptureFlow>();
#endif
        if (instance != null)
        {
            instance.EnsureBuilt();
            return instance;
        }

        GameObject flowObject = new GameObject("Dialogue Speech Capture Flow");
        instance = flowObject.AddComponent<DialogueSpeechCaptureFlow>();
        instance.EnsureBuilt();
        DontDestroyOnLoad(flowObject);
        return instance;
    }

    public static void AdvanceActivePrompt()
    {
        if (instance == null || instance.state != PromptState.Review)
        {
            return;
        }

        instance.ConfirmTranscript();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureBuilt();
    }

    private void Update()
    {
        if (state == PromptState.Hidden)
        {
            return;
        }

        if (dialogueView == null)
        {
            dialogueView = SceneDialogueView.ActiveInstance;
            ApplyInputReference();
        }

        if (!HasUsablePromptButtons())
        {
            promptButtons = ResolvePromptButtons();
        }

        UpdatePromptButtonPosition();
        UpdateBodyCopy();
        HandleAdvanceInput();
    }

    private void OnDestroy()
    {
        if (speechController != null)
        {
            speechController.TranscriptSubmitted -= HandleTranscriptSubmitted;
            speechController.TranscriptChanged -= HandleTranscriptChanged;
            speechController.SpeechError -= HandleSpeechError;
        }

        if (instance == this)
        {
            instance = null;
        }
    }

    public void Show(string title, string instructions, Action<string> onSubmitted)
    {
        EnsureBuilt();
        dialogueView = SceneDialogueView.ActiveInstance;
        ApplyInputReference();

        currentTitle = string.IsNullOrWhiteSpace(title) ? "AraBOT" : title.Trim();
        currentInstructions = string.IsNullOrWhiteSpace(instructions)
            ? "Click the mic above AraBOT to speak."
            : instructions.Trim();
        onTranscriptConfirmed = onSubmitted;
        liveTranscript = string.Empty;
        pendingTranscript = string.Empty;
        isTypingHintVisible = false;
        ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;

        speechController.ResetForReuse(
            "Click the mic above AraBOT to speak.",
            DefaultWaitingText,
            string.Empty);

        ShowReadyPrompt();
        ShowPromptButton(AraBotPromptButton.PromptRole.Mic, BeginListening);
        state = PromptState.ReadyToListen;
    }

    public void ShowProcessing(string studentName)
    {
        EnsureBuilt();
        dialogueView = SceneDialogueView.ActiveInstance;
        ApplyInputReference();

        liveTranscript = string.Empty;
        pendingTranscript = string.Empty;
        isTypingHintVisible = false;
        onTranscriptConfirmed = null;
        ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;

        HidePromptButtons();
        SetAraBotThinking(false);
        string safeStudentName = string.IsNullOrWhiteSpace(studentName) ? "Student" : studentName.Trim();
        ShowDialogueHint(
            safeStudentName,
            safeStudentName + " is thinking...",
            useStudentStyle: true);

        state = PromptState.Processing;
    }

    public void Hide()
    {
        onTranscriptConfirmed = null;
        liveTranscript = string.Empty;
        pendingTranscript = string.Empty;
        isTypingHintVisible = false;
        state = PromptState.Hidden;

        if (speechController != null)
        {
            speechController.StopListeningWithoutSubmitting();
        }

        HidePromptButtons();
        SetAraBotThinking(false);

        if (dialogueView != null)
        {
            dialogueView.SetExternalBodyAction(null);
            dialogueView.SetVisible(false);
        }
    }

    private void EnsureBuilt()
    {
        if (speechController != null)
        {
            return;
        }

        GameObject controllerObject = new GameObject("Speech Controller");
        controllerObject.transform.SetParent(transform, false);
        speechController = controllerObject.AddComponent<BrowserSpeechToTextPrototype>();
        speechController.TranscriptSubmitted += HandleTranscriptSubmitted;
        speechController.TranscriptChanged += HandleTranscriptChanged;
        speechController.SpeechError += HandleSpeechError;
    }

    private void BeginListening()
    {
        if (state == PromptState.Listening && speechController != null && speechController.IsListening)
        {
            return;
        }

        liveTranscript = string.Empty;
        pendingTranscript = string.Empty;
        hasHeardSpeech = false;
        ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;

        ApplyInputReference();
        speechController.ResetForReuse(
            "Listening for AraBOT...",
            DefaultWaitingText,
            string.Empty);

        state = PromptState.Listening;
        SetAraBotThinking(false);
        ShowPromptButtonVisualOnly(AraBotPromptButton.PromptRole.Thinking);
        ShowTypingFallbackHint();

        speechController.StartListening();
    }

    private void HandleTranscriptSubmitted(string transcript)
    {
        pendingTranscript = string.IsNullOrWhiteSpace(transcript)
            ? NoSpeechSubmittedText
            : transcript.Trim();
        state = PromptState.Review;
        ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;

        if (dialogueView != null)
        {
            dialogueView.SetExternalBodyAction(null);
            dialogueView.SetExternalInputVisible(false);
        }

        isTypingHintVisible = false;

        ShowDialogue(pendingTranscript, true);
        ShowPromptButton(AraBotPromptButton.PromptRole.Redo, BeginListening);
    }

    private void HandleTranscriptChanged(string transcript)
    {
        liveTranscript = string.IsNullOrWhiteSpace(transcript) ? string.Empty : transcript.Trim();

        if (state == PromptState.Listening && !hasHeardSpeech && !string.IsNullOrWhiteSpace(liveTranscript))
        {
            hasHeardSpeech = true;
            isTypingHintVisible = false;
            dialogueView?.SetExternalBodyAction(null);
            ShowPromptButtonVisualOnly(AraBotPromptButton.PromptRole.Thinking);
        }
    }

    private void HandleSpeechError(string _)
    {
        if (state != PromptState.Listening)
        {
            return;
        }

        state = PromptState.ReadyToListen;
        isTypingHintVisible = false;
        SetAraBotThinking(false);
        ShowReadyPrompt();
        ShowPromptButton(AraBotPromptButton.PromptRole.Mic, BeginListening);
    }

    private void UpdateBodyCopy()
    {
        if (dialogueView == null)
        {
            return;
        }

        switch (state)
        {
            case PromptState.Listening:
                if (string.IsNullOrWhiteSpace(liveTranscript))
                {
                    if (!isTypingHintVisible)
                    {
                        ShowTypingFallbackHint();
                    }
                }
                else
                {
                    isTypingHintVisible = false;
                    dialogueView.SetExternalBodyAction(null);
                    dialogueView.SetExternalBodyTextSmooth(liveTranscript, false);
                }
                break;

        }
    }

    private void HandleAdvanceInput()
    {
        if (state == PromptState.Typing)
        {
            if (Time.unscaledTime >= ignoreAdvanceUntil && WasEnterPressed())
            {
                SubmitTypedReply();
            }

            return;
        }

        bool pointerPressed = WasPointerPressed();
        if (Time.unscaledTime < ignoreAdvanceUntil
            || (pointerPressed && IsPointerOverPromptButton())
            || (pointerPressed && dialogueView != null && dialogueView.IsPointerOverControlButton())
            || !WasAdvancePressed())
        {
            return;
        }

        switch (state)
        {
            case PromptState.Listening:
                speechController.SubmitTranscript();
                ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;
                break;

            case PromptState.Review:
                ConfirmTranscript();
                break;
        }
    }

    private void ConfirmTranscript()
    {
        string confirmedTranscript = pendingTranscript;
        Action<string> callback = onTranscriptConfirmed;
        Hide();
        callback?.Invoke(confirmedTranscript);
    }

    private void ShowTypingFallbackHint()
    {
        ShowDialogueHint(currentTitle, DefaultWaitingText + "\n\n" + TypeInsteadText);
        dialogueView?.SetExternalBodyAction(BeginTyping);
        isTypingHintVisible = true;
    }

    private void ShowReadyPrompt()
    {
        ShowDialogueHint(currentTitle, currentInstructions + "\n\n" + TypeInsteadText);
        dialogueView?.SetExternalBodyAction(BeginTyping);
        isTypingHintVisible = true;
    }

    private void BeginTyping()
    {
        if (state != PromptState.Listening && state != PromptState.ReadyToListen)
        {
            return;
        }

        speechController?.StopListeningWithoutSubmitting();
        state = PromptState.Typing;
        isTypingHintVisible = false;
        ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;
        ShowPromptButton(AraBotPromptButton.PromptRole.Mic, BeginListening);

        if (dialogueView == null)
        {
            dialogueView = SceneDialogueView.ActiveInstance;
        }

        if (dialogueView == null)
        {
            return;
        }

        dialogueView.SetExternalBodyAction(null);
        dialogueView.SetExternalInputVisible(true, TypedReplyPlaceholder, string.Empty);
        dialogueView.FocusExternalInputField();
    }

    private void SubmitTypedReply()
    {
        TMP_InputField inputField = dialogueView != null ? dialogueView.ExternalInputField : null;
        string typedReply = inputField != null ? inputField.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(typedReply))
        {
            dialogueView?.SetExternalInputVisible(true, "Type something before pressing Enter", string.Empty);
            dialogueView?.FocusExternalInputField();
            return;
        }

        pendingTranscript = typedReply;
        ConfirmTranscript();
    }

    private void ShowDialogue(string speaker, string body, bool canAdvance)
    {
        dialogueView = SceneDialogueView.ActiveInstance;
        if (dialogueView == null)
        {
            return;
        }

        ApplyInputReference();
        dialogueView.ShowExternalContentSmooth(speaker, body, canAdvance);
    }

    private void ShowDialogue(string body, bool canAdvance)
    {
        if (body == NoSpeechSubmittedText)
        {
            ShowDialogueHint(currentTitle, body, canAdvance);
            return;
        }

        ShowDialogue(currentTitle, body, canAdvance);
    }

    private void ShowDialogueHint(
        string speaker,
        string hint,
        bool canAdvance = false,
        bool useStudentStyle = false)
    {
        dialogueView = SceneDialogueView.ActiveInstance;
        if (dialogueView == null)
        {
            return;
        }

        ApplyInputReference();
        dialogueView.ShowExternalHint(speaker, hint, canAdvance, useStudentStyle);
    }

    private void ApplyInputReference()
    {
        if (speechController == null)
        {
            return;
        }

        speechController.SetReferences(
            null,
            null,
            null,
            null,
            dialogueView != null ? dialogueView.ExternalInputField : null,
            null,
            null);
    }

    private void ShowPromptButton(AraBotPromptButton.PromptRole role, Action onClick)
    {
        SetAraBotThinking(false);
        promptButtons = ResolvePromptButtons();

        for (int index = 0; index < promptButtons.Length; index++)
        {
            AraBotPromptButton candidate = promptButtons[index];
            if (candidate != null)
            {
                candidate.Hide();
            }
        }

        AraBotPromptButton promptButton = FindPromptButton(role);
        if (promptButton == null)
        {
            return;
        }

        Action wrappedClick = null;
        if (onClick != null)
        {
            wrappedClick = () =>
            {
                ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;
                onClick.Invoke();
            };
        }

        promptButton.Show(wrappedClick);
    }

    private void ShowPromptButtonVisualOnly(AraBotPromptButton.PromptRole role)
    {
        SetAraBotThinking(role == AraBotPromptButton.PromptRole.Thinking);
        promptButtons = ResolvePromptButtons();

        for (int index = 0; index < promptButtons.Length; index++)
        {
            AraBotPromptButton candidate = promptButtons[index];
            if (candidate != null)
            {
                candidate.Hide();
            }
        }

        AraBotPromptButton promptButton = FindPromptButton(role);
        if (promptButton != null)
        {
            promptButton.ShowVisualOnly();
        }
    }

    private void HidePromptButtons()
    {
        promptButtons = ResolvePromptButtons();

        for (int index = 0; index < promptButtons.Length; index++)
        {
            AraBotPromptButton candidate = promptButtons[index];
            if (candidate != null)
            {
                candidate.Hide();
            }
        }
    }

    private void SetAraBotThinking(bool visible)
    {
        if (araBotActivityBubble == null)
        {
            araBotActivityBubble = ResolveAraBotActivityBubble();
        }

        if (araBotActivityBubble != null)
        {
            araBotActivityBubble.SetThinking(visible);
        }
    }

    private CharacterActivityBubble ResolveAraBotActivityBubble()
    {
#if UNITY_2023_1_OR_NEWER
        DialogueActor[] actors = FindObjectsByType<DialogueActor>(FindObjectsSortMode.None);
#else
        DialogueActor[] actors = FindObjectsOfType<DialogueActor>();
#endif
        for (int index = 0; index < actors.Length; index++)
        {
            DialogueActor actor = actors[index];
            if (actor != null && string.Equals(actor.DisplayName, "AraBOT", StringComparison.OrdinalIgnoreCase))
            {
                return actor.GetComponent<CharacterActivityBubble>();
            }
        }

        return null;
    }

    private AraBotPromptButton FindPromptButton(AraBotPromptButton.PromptRole role)
    {
        for (int index = 0; index < promptButtons.Length; index++)
        {
            AraBotPromptButton promptButton = promptButtons[index];
            if (promptButton == null)
            {
                continue;
            }

            AraBotPromptButton.PromptRole promptRole = GetPromptRole(promptButton);
            if (promptRole == role)
            {
                return promptButton;
            }
        }

        if (role == AraBotPromptButton.PromptRole.Thinking)
        {
            return FindPromptButton(AraBotPromptButton.PromptRole.Mic);
        }

        return null;
    }

    private AraBotPromptButton[] ResolvePromptButtons()
    {
        if (HasUsablePromptButtons())
        {
            AttachPromptButtonsToDialogueCanvas(promptButtons);
            return promptButtons;
        }

#if UNITY_2023_1_OR_NEWER
        DialogueActor[] actors = FindObjectsByType<DialogueActor>(FindObjectsSortMode.None);
#else
        DialogueActor[] actors = FindObjectsOfType<DialogueActor>();
#endif
        for (int index = 0; index < actors.Length; index++)
        {
            DialogueActor actor = actors[index];
            if (actor == null || !string.Equals(actor.DisplayName, "AraBOT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AraBotPromptButton[] foundButtons = actor.GetComponentsInChildren<AraBotPromptButton>(true);
            if (foundButtons != null && foundButtons.Length > 0)
            {
                promptWorldAnchor = actor.transform;
                araBotActivityBubble = actor.GetComponent<CharacterActivityBubble>();
                AttachPromptButtonsToDialogueCanvas(foundButtons);
                return foundButtons;
            }
        }

        promptWorldAnchor = null;
        return Array.Empty<AraBotPromptButton>();
    }

    private bool HasUsablePromptButtons()
    {
        if (promptButtons == null || promptButtons.Length == 0)
        {
            return false;
        }

        for (int index = 0; index < promptButtons.Length; index++)
        {
            if (promptButtons[index] != null)
            {
                return true;
            }
        }

        return false;
    }

    private void AttachPromptButtonsToDialogueCanvas(AraBotPromptButton[] buttons)
    {
        dialogueView = SceneDialogueView.ActiveInstance;
        RectTransform controlsRoot = dialogueView != null ? dialogueView.DialogueControlsRoot : null;
        if (controlsRoot == null || buttons == null)
        {
            return;
        }

        Canvas dialogueCanvas = controlsRoot.GetComponentInParent<Canvas>();
        Canvas previousPromptCanvas = null;
        for (int index = 0; index < buttons.Length; index++)
        {
            AraBotPromptButton promptButton = buttons[index];
            RectTransform promptRect = promptButton != null
                ? promptButton.transform as RectTransform
                : null;
            if (promptRect == null)
            {
                continue;
            }

            if (promptRect.parent != controlsRoot)
            {
                Canvas currentCanvas = promptRect.GetComponentInParent<Canvas>();
                if (currentCanvas != null && currentCanvas != dialogueCanvas)
                {
                    previousPromptCanvas = currentCanvas;
                }

                promptRect.SetParent(controlsRoot, false);
                promptRect.SetAsLastSibling();
            }

            promptRect.anchorMin = new Vector2(0.5f, 0.5f);
            promptRect.anchorMax = new Vector2(0.5f, 0.5f);
            promptRect.pivot = new Vector2(0.5f, 0.5f);
            promptRect.sizeDelta = promptScreenSize;
            promptRect.localScale = Vector3.one;
            SetLayerRecursively(promptRect.gameObject, controlsRoot.gameObject.layer);

            QuestionButtonAnimator animator = promptButton.GetComponent<QuestionButtonAnimator>();
            if (animator != null)
            {
                animator.ConfigureForScreenSpace(promptScreenSize);
            }
        }

        if (previousPromptCanvas != null && previousPromptCanvas.transform.childCount == 0)
        {
            previousPromptCanvas.gameObject.SetActive(false);
        }

        araBotActivityBubble?.ConfigureForScreenSpace();
        UpdatePromptButtonPosition();
    }

    private void UpdatePromptButtonPosition()
    {
        if (!HasUsablePromptButtons() || promptWorldAnchor == null || dialogueView == null)
        {
            return;
        }

        RectTransform controlsRoot = dialogueView.DialogueControlsRoot;
        Camera worldCamera = Camera.main;
        if (controlsRoot == null || worldCamera == null)
        {
            return;
        }

        Vector3 screenPoint = worldCamera.WorldToScreenPoint(promptWorldAnchor.position + promptWorldOffset);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        Canvas dialogueCanvas = controlsRoot.GetComponentInParent<Canvas>();
        Camera eventCamera = dialogueCanvas != null && dialogueCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? (dialogueCanvas.worldCamera != null ? dialogueCanvas.worldCamera : worldCamera)
            : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                controlsRoot,
                screenPoint,
                eventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        localPoint += promptScreenOffset;
        Vector2 halfSize = promptScreenSize * 0.5f;
        Rect availableRect = controlsRoot.rect;
        localPoint.x = Mathf.Clamp(localPoint.x, availableRect.xMin + halfSize.x, availableRect.xMax - halfSize.x);
        localPoint.y = Mathf.Clamp(localPoint.y, availableRect.yMin + halfSize.y, availableRect.yMax - halfSize.y);

        for (int index = 0; index < promptButtons.Length; index++)
        {
            AraBotPromptButton promptButton = promptButtons[index];
            RectTransform promptRect = promptButton != null
                ? promptButton.transform as RectTransform
                : null;
            if (promptRect == null)
            {
                continue;
            }

            QuestionButtonAnimator animator = promptButton.GetComponent<QuestionButtonAnimator>();
            if (animator != null)
            {
                animator.SetBaseAnchoredPosition(localPoint);
            }
            else
            {
                promptRect.anchoredPosition = localPoint;
            }
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null)
        {
            return;
        }

        root.layer = layer;
        Transform rootTransform = root.transform;
        for (int index = 0; index < rootTransform.childCount; index++)
        {
            SetLayerRecursively(rootTransform.GetChild(index).gameObject, layer);
        }
    }

    private static AraBotPromptButton.PromptRole GetPromptRole(AraBotPromptButton promptButton)
    {
        if (promptButton == null)
        {
            return AraBotPromptButton.PromptRole.Default;
        }

        if (promptButton.Role != AraBotPromptButton.PromptRole.Default)
        {
            return promptButton.Role;
        }

        string promptName = promptButton.gameObject.name;
        if (promptName.IndexOf("mic", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return AraBotPromptButton.PromptRole.Mic;
        }

        if (promptName.IndexOf("redo", StringComparison.OrdinalIgnoreCase) >= 0
            || promptName.IndexOf("reset", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return AraBotPromptButton.PromptRole.Redo;
        }

        if (promptName.IndexOf("keyboard", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return AraBotPromptButton.PromptRole.Keyboard;
        }

        if (promptName.IndexOf("thinking", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return AraBotPromptButton.PromptRole.Thinking;
        }

        return AraBotPromptButton.PromptRole.Default;
    }

    private bool WasAdvancePressed()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            pressed |= Keyboard.current.spaceKey.wasPressedThisFrame;
            pressed |= Keyboard.current.enterKey.wasPressedThisFrame;
        }

#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#endif

        return pressed || WasPointerPressed();
    }

    private static bool WasEnterPressed()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            pressed |= Keyboard.current.enterKey.wasPressedThisFrame;
            pressed |= Keyboard.current.numpadEnterKey.wasPressedThisFrame;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif

        return pressed;
    }

    private static bool WasPointerPressed()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            pressed |= Mouse.current.leftButton.wasPressedThisFrame;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetMouseButtonDown(0);
#endif

        return pressed;
    }

    private bool IsPointerOverPromptButton()
    {
        Vector2 pointerPosition;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            pointerPosition = Mouse.current.position.ReadValue();
        }
        else
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        {
            pointerPosition = Input.mousePosition;
        }
#else
        {
            return false;
        }
#endif

        promptButtons = ResolvePromptButtons();
        for (int index = 0; index < promptButtons.Length; index++)
        {
            AraBotPromptButton promptButton = promptButtons[index];
            RectTransform promptRect = promptButton != null && promptButton.gameObject.activeInHierarchy
                ? promptButton.transform as RectTransform
                : null;
            Canvas promptCanvas = promptButton != null ? promptButton.GetComponentInParent<Canvas>() : null;
            Camera eventCamera = promptCanvas != null && promptCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? (promptCanvas.worldCamera != null ? promptCanvas.worldCamera : Camera.main)
                : null;
            if (promptRect != null
                && RectTransformUtility.RectangleContainsScreenPoint(promptRect, pointerPosition, eventCamera))
            {
                return true;
            }
        }

        return false;
    }

    private enum PromptState
    {
        Hidden,
        ReadyToListen,
        Listening,
        Typing,
        Review,
        Processing
    }
}

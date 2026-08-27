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
    private const string DefaultTypingText = "The mic did not catch anything clearly, so you can type your reply instead.";
    private const string NoSpeechSubmittedText = "No speech or fallback text was submitted.";

    private static DialogueSpeechCaptureFlow instance;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float fallbackKeyboardDelaySeconds = 7f;
    [SerializeField, Min(0f)] private float inputDebounceSeconds = 0.15f;

    [Header("Listening")]
    [SerializeField] private bool autoStartListening = true;

    private BrowserSpeechToTextPrototype speechController;
    private SceneDialogueView dialogueView;
    private AraBotPromptButton[] promptButtons = Array.Empty<AraBotPromptButton>();
    private CharacterActivityBubble araBotActivityBubble;
    private Action<string> onTranscriptConfirmed;
    private PromptState state = PromptState.Hidden;
    private string currentTitle = string.Empty;
    private string currentInstructions = string.Empty;
    private string liveTranscript = string.Empty;
    private string pendingTranscript = string.Empty;
    private float listeningStartedAt;
    private float ignoreAdvanceUntil;

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

        if (promptButtons == null || promptButtons.Length == 0)
        {
            promptButtons = ResolvePromptButtons();
        }

        UpdateBodyCopy();
        UpdateListeningState();
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
            ? "Tap the mic above AraBOT, speak your answer, then continue when you are done."
            : instructions.Trim();
        onTranscriptConfirmed = onSubmitted;
        liveTranscript = string.Empty;
        pendingTranscript = string.Empty;
        ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;

        speechController.ResetForReuse(
            "Tap the mic when you want to start.",
            DefaultWaitingText,
            string.Empty);

        ShowDialogueHint(currentTitle, currentInstructions);
        ShowPromptButton(AraBotPromptButton.PromptRole.Mic, BeginListening);
        state = PromptState.ReadyToListen;

        if (autoStartListening)
        {
            BeginListening();
        }
    }

    public void ShowProcessing()
    {
        EnsureBuilt();
        dialogueView = SceneDialogueView.ActiveInstance;
        ApplyInputReference();

        liveTranscript = string.Empty;
        pendingTranscript = string.Empty;
        onTranscriptConfirmed = null;
        ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;

        HidePromptButtons();
        SetAraBotThinking(false);
        if (dialogueView != null)
        {
            dialogueView.SetVisible(false);
        }

        state = PromptState.Processing;
    }

    public void Hide()
    {
        onTranscriptConfirmed = null;
        liveTranscript = string.Empty;
        pendingTranscript = string.Empty;
        state = PromptState.Hidden;

        if (speechController != null)
        {
            speechController.StopListeningWithoutSubmitting();
        }

        HidePromptButtons();
        SetAraBotThinking(false);

        if (dialogueView != null)
        {
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
        liveTranscript = string.Empty;
        pendingTranscript = string.Empty;
        listeningStartedAt = Time.unscaledTime;
        ignoreAdvanceUntil = Time.unscaledTime + inputDebounceSeconds;

        if (dialogueView != null && dialogueView.ExternalInputField != null)
        {
            dialogueView.ExternalInputField.text = string.Empty;
        }

        ApplyInputReference();
        speechController.ResetForReuse(
            "Listening for AraBOT...",
            DefaultWaitingText,
            string.Empty);

        if (speechController.RequiresTypedFallbackMode)
        {
            ActivateKeyboardFallback();
            return;
        }

        state = PromptState.Listening;
        HidePromptButtons();
        SetAraBotThinking(true);
        ShowDialogue(currentTitle, DefaultWaitingText, false);
        speechController.StartListening();
    }

    private void ActivateKeyboardFallback()
    {
        speechController.StopListeningWithoutSubmitting();
        state = PromptState.TypingFallback;
        ShowDialogue(currentTitle, DefaultTypingText, false);

        if (dialogueView != null)
        {
            dialogueView.SetExternalInputVisible(true, "Type your reply here...", speechController.CurrentDisplayTranscript);
            dialogueView.FocusExternalInputField();
        }

        ApplyInputReference();
        ShowPromptButtonVisualOnly(AraBotPromptButton.PromptRole.Keyboard);
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
            dialogueView.SetExternalInputVisible(false);
        }

        ShowDialogue(currentTitle, pendingTranscript, true);
        ShowPromptButton(AraBotPromptButton.PromptRole.Redo, BeginListening);
    }

    private void HandleTranscriptChanged(string transcript)
    {
        liveTranscript = string.IsNullOrWhiteSpace(transcript) ? string.Empty : transcript.Trim();
    }

    private void HandleSpeechError(string _)
    {
        if (state != PromptState.Listening)
        {
            return;
        }

        state = PromptState.ReadyToListen;
        SetAraBotThinking(false);
        ShowDialogueHint(currentTitle, currentInstructions);
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
                dialogueView.SetExternalBodyText(
                    string.IsNullOrWhiteSpace(liveTranscript)
                        ? DefaultWaitingText
                        : liveTranscript,
                    false);
                break;

        }
    }

    private void UpdateListeningState()
    {
        if (state != PromptState.Listening)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(liveTranscript))
        {
            return;
        }

        if (Time.unscaledTime - listeningStartedAt >= fallbackKeyboardDelaySeconds)
        {
            ActivateKeyboardFallback();
        }
    }

    private void HandleAdvanceInput()
    {
        if (Time.unscaledTime < ignoreAdvanceUntil || !WasAdvancePressed())
        {
            return;
        }

        if (state == PromptState.TypingFallback && dialogueView != null)
        {
            TMP_InputField inputField = dialogueView.ExternalInputField;
            if (inputField != null && inputField.isFocused)
            {
                return;
            }
        }

        switch (state)
        {
            case PromptState.Listening:
            case PromptState.TypingFallback:
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

    private void ShowDialogue(string speaker, string body, bool canAdvance)
    {
        dialogueView = SceneDialogueView.ActiveInstance;
        if (dialogueView == null)
        {
            return;
        }

        ApplyInputReference();
        dialogueView.ShowExternalContent(speaker, body, canAdvance);
    }

    private void ShowDialogueHint(string speaker, string hint)
    {
        dialogueView = SceneDialogueView.ActiveInstance;
        if (dialogueView == null)
        {
            return;
        }

        ApplyInputReference();
        dialogueView.ShowExternalHint(speaker, hint);
    }

    private void ApplyInputReference()
    {
        if (speechController == null)
        {
            return;
        }

        TMP_InputField inputField = dialogueView != null ? dialogueView.ExternalInputField : null;
        speechController.SetReferences(
            null,
            null,
            null,
            null,
            inputField,
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
                return foundButtons;
            }
        }

        return Array.Empty<AraBotPromptButton>();
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
        bool keyboardPressed = false;
        bool pointerPressed = false;
        Vector2 pointerPosition = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            pointerPressed |= Mouse.current.leftButton.wasPressedThisFrame;
            pointerPosition = Mouse.current.position.ReadValue();
        }

        if (Keyboard.current != null)
        {
            keyboardPressed |= Keyboard.current.spaceKey.wasPressedThisFrame;
            keyboardPressed |= Keyboard.current.enterKey.wasPressedThisFrame;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            pointerPressed = true;
            pointerPosition = Input.mousePosition;
        }

        keyboardPressed |= Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#endif

        return keyboardPressed || (pointerPressed && !IsPointerOverPromptButton(pointerPosition));
    }

    private bool IsPointerOverPromptButton(Vector2 screenPosition)
    {
        promptButtons = ResolvePromptButtons();
        for (int index = 0; index < promptButtons.Length; index++)
        {
            AraBotPromptButton promptButton = promptButtons[index];
            if (promptButton == null || !promptButton.gameObject.activeInHierarchy)
            {
                continue;
            }

            RectTransform buttonRect = promptButton.transform as RectTransform;
            if (buttonRect == null)
            {
                continue;
            }

            Canvas canvas = promptButton.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera != null ? canvas.worldCamera : Camera.main
                : null;
            if (RectTransformUtility.RectangleContainsScreenPoint(buttonRect, screenPosition, eventCamera))
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
        TypingFallback,
        Review,
        Processing
    }
}

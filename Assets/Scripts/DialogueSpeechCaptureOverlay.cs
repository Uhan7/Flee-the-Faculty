using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class DialogueSpeechCaptureOverlay : MonoBehaviour
{
    private const string OverlayPrefabResourcePath = "Prefabs/Dialogue Speech Capture Overlay";
    private const string DefaultTitle = "Speak As AraBOT";
    private const string DefaultInstructions = "Tap the mic above AraBOT, speak your reply, then click the bubble or press next when you are done.";
    private const string DefaultWaitingTranscript = "AraBOT is listening...";
    private const string DefaultReviewTitle = "AraBOT's Reply";
    private const string DefaultTypingTitle = "Type AraBOT's Reply";
    private const string DefaultTypingInstructions = "The mic did not catch anything clearly, so you can type AraBOT's reply instead.";
    private const string NoSpeechSubmittedText = "No speech or fallback text was submitted.";
    private const float ReferenceWidth = 1600f;
    private const float ReferenceHeight = 900f;

    private static DialogueSpeechCaptureOverlay instance;

    [Header("Look")]
    [SerializeField] private TMP_FontAsset headingFont;
    [SerializeField] private TMP_FontAsset bodyFont;
    [SerializeField] private Sprite transcriptPanelSprite;
    [SerializeField] private Sprite promptBubbleSprite;
    [SerializeField] private Sprite micIconSprite;
    [SerializeField] private Sprite redoIconSprite;
    [SerializeField] private Sprite keyboardIconSprite;
    [SerializeField] private Sprite loadingDotSprite;
    [SerializeField] private Color transcriptPanelColor = Color.white;
    [SerializeField] private Color overlayBlockerColor = new Color(0f, 0f, 0f, 0f);
    [SerializeField] private Color titleColor = new Color(0.08f, 0.35f, 0.49f, 1f);
    [SerializeField] private Color bodyColor = new Color(0.12f, 0.23f, 0.31f, 1f);
    [SerializeField] private Color hintColor = new Color(0.14f, 0.45f, 0.58f, 1f);
    [SerializeField] private Color promptLabelColor = new Color(0.92f, 0.99f, 1f, 1f);
    [SerializeField] private Color dotColor = new Color(0.14f, 0.56f, 0.7f, 1f);

    [Header("Layout")]
    [SerializeField] private Vector3 speechAnchorWorldOffset = new Vector3(0f, 2.15f, 0f);
    [SerializeField] private Vector2 promptBubbleScreenOffset = new Vector2(0f, 26f);
    [SerializeField] private Vector2 transcriptBubbleSize = new Vector2(760f, 280f);
    [SerializeField] private Vector2 transcriptBubbleScreenOffset = new Vector2(0f, 168f);
    [SerializeField, Min(0f)] private float fallbackKeyboardDelaySeconds = 7f;
    [SerializeField, Min(0f)] private float promptBubblePadding = 28f;
    [SerializeField] private int sortingOrder = 1100;

    private BrowserSpeechToTextPrototype speechController;
    private Canvas canvas;
    private CanvasScaler canvasScaler;
    private RectTransform rootRect;
    private GameObject overlayRoot;
    private Image blockerImage;
    private RectTransform promptBubbleRect;
    private Image promptBubbleImage;
    private Button promptBubbleButton;
    private Image promptIconImage;
    private TMP_Text promptLabelText;
    private RectTransform promptDotsRoot;
    private Image[] promptDotImages;
    private RectTransform transcriptBubbleRect;
    private Button transcriptBubbleButton;
    private TMP_Text transcriptTitleText;
    private TMP_Text transcriptBodyText;
    private TMP_Text transcriptHintText;
    private GameObject fallbackSectionRoot;
    private TMP_InputField fallbackInputField;
    private TMP_Text fallbackHeadingText;
    private Image fallbackIconImage;

    private Action<string> onTranscriptConfirmed;
    private Transform speechAnchor;
    private string currentTitle = DefaultTitle;
    private string currentInstructions = DefaultInstructions;
    private string pendingTranscript = string.Empty;
    private string liveTranscript = string.Empty;
    private float listeningStartedAt;

    private PromptState state = PromptState.Hidden;

    public static DialogueSpeechCaptureOverlay GetOrCreate()
    {
        if (instance != null)
        {
            return instance;
        }

#if UNITY_2023_1_OR_NEWER
        instance = FindFirstObjectByType<DialogueSpeechCaptureOverlay>();
#else
        instance = FindObjectOfType<DialogueSpeechCaptureOverlay>();
#endif
        if (instance != null)
        {
            instance.EnsureBuilt();
            return instance;
        }

        DialogueSpeechCaptureOverlay prefabInstance = CreateInstanceFromPrefab();
        if (prefabInstance != null)
        {
            instance = prefabInstance;
            instance.EnsureBuilt();
            return instance;
        }

        GameObject overlayObject = new GameObject("Dialogue Speech Capture Overlay");
        instance = overlayObject.AddComponent<DialogueSpeechCaptureOverlay>();
        instance.EnsureBuilt();
        return instance;
    }

    private static DialogueSpeechCaptureOverlay CreateInstanceFromPrefab()
    {
        GameObject overlayPrefab = Resources.Load<GameObject>(OverlayPrefabResourcePath);
        if (overlayPrefab == null)
        {
            return null;
        }

        GameObject overlayInstance = Instantiate(overlayPrefab);
        overlayInstance.name = overlayPrefab.name;
        return overlayInstance.GetComponent<DialogueSpeechCaptureOverlay>();
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
        SetState(PromptState.Hidden);
    }

    private void Update()
    {
        if (state == PromptState.Hidden)
        {
            return;
        }

        UpdatePromptBubblePosition();
        UpdateListeningState();
        UpdateLoadingDots();
        HandleAdvanceInput();
    }

    private void OnDestroy()
    {
        if (speechController != null)
        {
            speechController.TranscriptSubmitted -= HandleTranscriptSubmitted;
            speechController.TranscriptChanged -= HandleTranscriptChanged;
            speechController.ListeningStateChanged -= HandleListeningStateChanged;
        }

        if (instance == this)
        {
            instance = null;
        }
    }

    public void Show(string title, string instructions, Action<string> onSubmitted)
    {
        EnsureBuilt();

        currentTitle = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim();
        currentInstructions = string.IsNullOrWhiteSpace(instructions) ? DefaultInstructions : instructions.Trim();
        onTranscriptConfirmed = onSubmitted;
        speechAnchor = ResolveSpeechAnchor();
        pendingTranscript = string.Empty;
        liveTranscript = string.Empty;

        if (fallbackInputField != null)
        {
            fallbackInputField.text = string.Empty;
        }

        speechController.ResetForReuse(
            "Tap the mic bubble when you want to start.",
            DefaultWaitingTranscript,
            string.Empty);

        if (overlayRoot != null)
        {
            overlayRoot.SetActive(true);
        }

        SetState(PromptState.ReadyToListen);
    }

    public void ShowProcessing(string title, string instructions)
    {
        EnsureBuilt();

        currentTitle = string.IsNullOrWhiteSpace(title) ? "Loading" : title.Trim();
        currentInstructions = string.IsNullOrWhiteSpace(instructions)
            ? "Getting everything ready..."
            : instructions.Trim();
        onTranscriptConfirmed = null;
        speechAnchor = ResolveSpeechAnchor();
        pendingTranscript = string.Empty;
        liveTranscript = string.Empty;

        if (overlayRoot != null)
        {
            overlayRoot.SetActive(true);
        }

        SetState(PromptState.Processing);
    }

    public void Hide()
    {
        onTranscriptConfirmed = null;
        pendingTranscript = string.Empty;
        liveTranscript = string.Empty;

        if (speechController != null)
        {
            speechController.StopListeningWithoutSubmitting();
        }

        if (overlayRoot != null)
        {
            overlayRoot.SetActive(false);
        }

        SetState(PromptState.Hidden);
    }

    private void EnsureBuilt()
    {
        if (speechController != null && overlayRoot != null)
        {
            return;
        }

        BuildUi();
    }

    private void BuildUi()
    {
        TMP_FontAsset resolvedHeadingFont = headingFont != null
            ? headingFont
            : (bodyFont != null ? bodyFont : TMP_Settings.defaultFontAsset);
        TMP_FontAsset resolvedBodyFont = bodyFont != null
            ? bodyFont
            : (headingFont != null ? headingFont : TMP_Settings.defaultFontAsset);

        canvas = GetOrAddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        canvasScaler = GetOrAddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 0.5f;

        GetOrAddComponent<GraphicRaycaster>();

        rootRect = transform as RectTransform;
        if (rootRect == null)
        {
            rootRect = gameObject.AddComponent<RectTransform>();
        }

        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        overlayRoot = new GameObject("Overlay Root");
        overlayRoot.transform.SetParent(transform, false);

        RectTransform overlayRect = overlayRoot.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        blockerImage = overlayRoot.AddComponent<Image>();
        blockerImage.color = overlayBlockerColor;
        blockerImage.raycastTarget = true;

        promptBubbleButton = CreateSpriteButton(
            "Prompt Bubble",
            overlayRect,
            promptBubbleSprite,
            Color.white,
            new Vector2(144f, 152f),
            out promptBubbleRect,
            out promptBubbleImage);
        promptBubbleButton.onClick.AddListener(HandlePromptBubblePressed);

        promptIconImage = CreateImage(
            "Prompt Icon",
            promptBubbleRect,
            null,
            new Color(0.11f, 0.53f, 0.68f, 1f),
            new Vector2(0.5f, 0.64f),
            new Vector2(0.5f, 0.64f),
            Vector2.zero,
            new Vector2(72f, 72f));

        promptDotsRoot = CreateEmptyRect(
            "Prompt Dots",
            promptBubbleRect,
            new Vector2(0.5f, 0.62f),
            new Vector2(0.5f, 0.62f),
            Vector2.zero,
            new Vector2(78f, 22f));
        promptDotImages = new Image[3];
        for (int index = 0; index < promptDotImages.Length; index++)
        {
            promptDotImages[index] = CreateImage(
                "Dot " + index,
                promptDotsRoot,
                loadingDotSprite,
                dotColor,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2((index - 1) * 22f, 0f),
                new Vector2(18f, 18f));
        }

        promptLabelText = CreateText(
            "Prompt Label",
            promptBubbleRect,
            resolvedBodyFont,
            string.Empty,
            18f,
            FontStyles.Bold,
            promptLabelColor,
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 16f),
            new Vector2(180f, 28f));

        transcriptBubbleButton = CreateSpriteButton(
            "Transcript Bubble",
            overlayRect,
            transcriptPanelSprite,
            transcriptPanelColor,
            transcriptBubbleSize,
            out transcriptBubbleRect,
            out _);
        transcriptBubbleButton.onClick.AddListener(HandleTranscriptBubblePressed);
        transcriptBubbleRect.anchorMin = new Vector2(0.5f, 0f);
        transcriptBubbleRect.anchorMax = new Vector2(0.5f, 0f);
        transcriptBubbleRect.pivot = new Vector2(0.5f, 0f);
        transcriptBubbleRect.anchoredPosition = transcriptBubbleScreenOffset;

        transcriptTitleText = CreateText(
            "Transcript Title",
            transcriptBubbleRect,
            resolvedHeadingFont,
            DefaultTitle,
            30f,
            FontStyles.Bold,
            titleColor,
            TextAlignmentOptions.Center,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0f, -34f),
            new Vector2(-56f, 38f));

        transcriptBodyText = CreateText(
            "Transcript Body",
            transcriptBubbleRect,
            resolvedBodyFont,
            string.Empty,
            24f,
            FontStyles.Normal,
            bodyColor,
            TextAlignmentOptions.TopLeft,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, -92f),
            new Vector2(-72f, -138f));

        transcriptHintText = CreateText(
            "Transcript Hint",
            transcriptBubbleRect,
            resolvedBodyFont,
            string.Empty,
            18f,
            FontStyles.Bold,
            hintColor,
            TextAlignmentOptions.Bottom,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 24f),
            new Vector2(-64f, 30f));

        fallbackSectionRoot = new GameObject("Fallback Section");
        fallbackSectionRoot.transform.SetParent(transcriptBubbleRect, false);

        RectTransform fallbackRect = fallbackSectionRoot.AddComponent<RectTransform>();
        fallbackRect.anchorMin = new Vector2(0f, 0f);
        fallbackRect.anchorMax = new Vector2(1f, 0f);
        fallbackRect.pivot = new Vector2(0.5f, 0f);
        fallbackRect.anchoredPosition = new Vector2(0f, 58f);
        fallbackRect.sizeDelta = new Vector2(-72f, 112f);

        fallbackHeadingText = CreateText(
            "Fallback Heading",
            fallbackRect,
            resolvedBodyFont,
            "Nothing came through. Type AraBOT's reply instead.",
            18f,
            FontStyles.Bold,
            hintColor,
            TextAlignmentOptions.TopLeft,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(40f, -6f),
            new Vector2(-40f, 22f));

        fallbackIconImage = CreateImage(
            "Fallback Icon",
            fallbackRect,
            keyboardIconSprite,
            new Color(0.11f, 0.53f, 0.68f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(14f, -8f),
            new Vector2(26f, 26f));

        fallbackInputField = CreateInputField(
            "Fallback Input",
            fallbackRect,
            resolvedBodyFont,
            "Type AraBOT's reply here...",
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0f),
            new Vector2(0f, 66f));

        GameObject controllerObject = new GameObject("Speech Controller");
        controllerObject.transform.SetParent(transform, false);
        speechController = controllerObject.AddComponent<BrowserSpeechToTextPrototype>();
        speechController.SetReferences(
            null,
            null,
            null,
            null,
            fallbackInputField,
            null,
            null);
        speechController.TranscriptSubmitted += HandleTranscriptSubmitted;
        speechController.TranscriptChanged += HandleTranscriptChanged;
        speechController.ListeningStateChanged += HandleListeningStateChanged;

        overlayRoot.SetActive(false);
    }

    private void HandlePromptBubblePressed()
    {
        switch (state)
        {
            case PromptState.ReadyToListen:
            case PromptState.Review:
                BeginListening();
                break;

            case PromptState.TypingFallback:
                speechController.FocusFallbackInputField();
                break;
        }
    }

    private void HandleTranscriptBubblePressed()
    {
        switch (state)
        {
            case PromptState.Listening:
            case PromptState.TypingFallback:
                RequestSpeechSubmission();
                break;

            case PromptState.Review:
                ConfirmTranscript();
                break;
        }
    }

    private void HandleTranscriptSubmitted(string transcript)
    {
        pendingTranscript = string.IsNullOrWhiteSpace(transcript)
            ? NoSpeechSubmittedText
            : transcript.Trim();
        SetState(PromptState.Review);
    }

    private void HandleTranscriptChanged(string transcript)
    {
        liveTranscript = string.IsNullOrWhiteSpace(transcript) ? string.Empty : transcript.Trim();

        if (state == PromptState.Listening || state == PromptState.TypingFallback)
        {
            ApplyCurrentState();
        }
    }

    private void HandleListeningStateChanged(bool _)
    {
        if (state == PromptState.Listening || state == PromptState.TypingFallback)
        {
            ApplyCurrentState();
        }
    }

    private void BeginListening()
    {
        pendingTranscript = string.Empty;
        liveTranscript = string.Empty;
        listeningStartedAt = Time.unscaledTime;

        if (fallbackInputField != null)
        {
            fallbackInputField.text = string.Empty;
        }

        speechController.ResetForReuse(
            "Listening for AraBOT...",
            DefaultWaitingTranscript,
            string.Empty);
        speechController.StartListening();
        SetState(speechController.RequiresTypedFallbackMode ? PromptState.TypingFallback : PromptState.Listening);

        if (state == PromptState.TypingFallback)
        {
            speechController.FocusFallbackInputField();
        }
    }

    private void ConfirmTranscript()
    {
        string confirmedTranscript = pendingTranscript;
        Action<string> callback = onTranscriptConfirmed;
        Hide();
        callback?.Invoke(confirmedTranscript);
    }

    private void RequestSpeechSubmission()
    {
        if (state != PromptState.Listening && state != PromptState.TypingFallback)
        {
            return;
        }

        speechController.SubmitTranscript();
    }

    private void UpdateListeningState()
    {
        if (state != PromptState.Listening)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(liveTranscript) &&
            !speechController.IsListening &&
            !string.IsNullOrWhiteSpace(speechController.CurrentDisplayTranscript))
        {
            liveTranscript = speechController.CurrentDisplayTranscript;
        }

        if (string.IsNullOrWhiteSpace(liveTranscript) &&
            Time.unscaledTime - listeningStartedAt >= fallbackKeyboardDelaySeconds)
        {
            ActivateKeyboardFallback();
        }
    }

    private void ActivateKeyboardFallback()
    {
        if (state != PromptState.Listening)
        {
            return;
        }

        speechController.StopListeningWithoutSubmitting();
        SetState(PromptState.TypingFallback);
        speechController.FocusFallbackInputField();
    }

    private void HandleAdvanceInput()
    {
        if (!WasAdvancePressed())
        {
            return;
        }

        if (state == PromptState.TypingFallback && fallbackInputField != null && fallbackInputField.isFocused)
        {
            return;
        }

        if (state == PromptState.Listening || state == PromptState.TypingFallback)
        {
            RequestSpeechSubmission();
            return;
        }

        if (state == PromptState.Review)
        {
            ConfirmTranscript();
        }
    }

    private void UpdatePromptBubblePosition()
    {
        if (promptBubbleRect == null || !promptBubbleRect.gameObject.activeSelf || rootRect == null)
        {
            return;
        }

        Transform anchor = ResolveSpeechAnchor();
        Vector2 targetPosition = new Vector2(0f, (rootRect.rect.height * 0.26f)) + promptBubbleScreenOffset;

        if (anchor != null)
        {
            Camera worldCamera = ResolveWorldCamera();
            Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(worldCamera, anchor.position + speechAnchorWorldOffset);
            if (screenPoint.z > 0f &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screenPoint, null, out Vector2 localPoint))
            {
                targetPosition = localPoint + promptBubbleScreenOffset;
            }
        }

        Vector2 bubbleSize = promptBubbleRect.rect.size;
        float minX = rootRect.rect.xMin + (bubbleSize.x * 0.5f) + promptBubblePadding;
        float maxX = rootRect.rect.xMax - (bubbleSize.x * 0.5f) - promptBubblePadding;
        float minY = rootRect.rect.yMin + (bubbleSize.y * 0.5f) + promptBubblePadding;
        float maxY = rootRect.rect.yMax - (bubbleSize.y * 0.5f) - promptBubblePadding;

        promptBubbleRect.anchoredPosition = new Vector2(
            Mathf.Clamp(targetPosition.x, minX, maxX),
            Mathf.Clamp(targetPosition.y, minY, maxY));
    }

    private void UpdateLoadingDots()
    {
        if (promptDotImages == null)
        {
            return;
        }

        bool shouldAnimate = promptDotsRoot != null && promptDotsRoot.gameObject.activeSelf;
        for (int index = 0; index < promptDotImages.Length; index++)
        {
            Image dotImage = promptDotImages[index];
            if (dotImage == null)
            {
                continue;
            }

            if (!shouldAnimate)
            {
                dotImage.transform.localScale = Vector3.one;
                Color idleColor = dotImage.color;
                idleColor.a = 1f;
                dotImage.color = idleColor;
                continue;
            }

            float phase = (Time.unscaledTime * 4.2f) - (index * 0.18f);
            float wave = (Mathf.Sin(phase * Mathf.PI * 2f) + 1f) * 0.5f;
            float scale = Mathf.Lerp(0.72f, 1.08f, wave);
            float alpha = Mathf.Lerp(0.4f, 1f, wave);

            dotImage.transform.localScale = Vector3.one * scale;
            Color color = dotImage.color;
            color.a = alpha;
            dotImage.color = color;
        }
    }

    private void SetState(PromptState nextState)
    {
        state = nextState;

        if (state == PromptState.Hidden)
        {
            return;
        }

        ApplyCurrentState();
        UpdatePromptBubblePosition();
    }

    private void ApplyCurrentState()
    {
        bool showPromptBubble = false;
        bool showPromptDots = false;
        bool showPromptIcon = false;
        bool showTranscriptBubble = false;
        bool showFallbackSection = false;

        Sprite promptIcon = null;
        string promptLabel = string.Empty;
        string title = currentTitle;
        string body = currentInstructions;
        string hint = string.Empty;

        switch (state)
        {
            case PromptState.ReadyToListen:
                showPromptBubble = true;
                showPromptIcon = true;
                promptIcon = micIconSprite;
                promptLabel = "Speak";
                break;

            case PromptState.Listening:
                showTranscriptBubble = true;
                title = currentTitle;
                body = string.IsNullOrWhiteSpace(liveTranscript)
                    ? DefaultWaitingTranscript
                    : liveTranscript;
                hint = string.IsNullOrWhiteSpace(liveTranscript)
                    ? "Listening now. If nothing comes through, typing will unlock in a few seconds."
                    : "Click this bubble or press next when you are done talking.";
                showPromptBubble = string.IsNullOrWhiteSpace(liveTranscript);
                showPromptDots = showPromptBubble;
                break;

            case PromptState.TypingFallback:
                showPromptBubble = true;
                showPromptIcon = true;
                promptIcon = keyboardIconSprite;
                promptLabel = "Type";
                showTranscriptBubble = true;
                showFallbackSection = true;
                title = DefaultTypingTitle;
                body = string.IsNullOrWhiteSpace(liveTranscript)
                    ? DefaultTypingInstructions
                    : liveTranscript;
                hint = string.IsNullOrWhiteSpace(liveTranscript)
                    ? "Type your response, then click this bubble or press next to continue."
                    : "Keep typing if you want, then click this bubble or press next to continue.";
                break;

            case PromptState.Review:
                showPromptBubble = true;
                showPromptIcon = true;
                promptIcon = redoIconSprite;
                promptLabel = "Redo";
                showTranscriptBubble = true;
                title = DefaultReviewTitle;
                body = string.IsNullOrWhiteSpace(pendingTranscript)
                    ? NoSpeechSubmittedText
                    : pendingTranscript;
                hint = "Click this bubble or press next to continue. Tap redo above AraBOT to try again.";
                break;

            case PromptState.Processing:
                showPromptBubble = true;
                showPromptDots = true;
                showTranscriptBubble = true;
                title = currentTitle;
                body = currentInstructions;
                hint = "Please wait...";
                break;
        }

        if (overlayRoot != null && !overlayRoot.activeSelf)
        {
            overlayRoot.SetActive(true);
        }

        SetGameObjectActive(promptBubbleRect, showPromptBubble);
        SetGameObjectActive(transcriptBubbleRect, showTranscriptBubble);
        SetGameObjectActive(fallbackSectionRoot, showFallbackSection);
        SetGameObjectActive(promptDotsRoot, showPromptDots);

        if (promptIconImage != null)
        {
            promptIconImage.gameObject.SetActive(showPromptIcon);
            promptIconImage.sprite = promptIcon;
        }

        if (promptLabelText != null)
        {
            promptLabelText.text = promptLabel;
        }

        if (transcriptTitleText != null)
        {
            transcriptTitleText.text = title;
        }

        if (transcriptBodyText != null)
        {
            transcriptBodyText.text = body;
        }

        if (transcriptHintText != null)
        {
            transcriptHintText.text = hint;
        }

        if (fallbackHeadingText != null)
        {
            fallbackHeadingText.text = "Nothing came through. Type AraBOT's reply instead.";
        }
    }

    private Transform ResolveSpeechAnchor()
    {
        if (speechAnchor != null)
        {
            return speechAnchor;
        }

#if UNITY_2023_1_OR_NEWER
        DialogueActor[] actors = FindObjectsByType<DialogueActor>(FindObjectsSortMode.None);
#else
        DialogueActor[] actors = FindObjectsOfType<DialogueActor>();
#endif
        for (int index = 0; index < actors.Length; index++)
        {
            DialogueActor actor = actors[index];
            if (actor == null)
            {
                continue;
            }

            bool isAraBot = string.Equals(actor.DisplayName, "AraBOT", StringComparison.OrdinalIgnoreCase)
                || actor.name.IndexOf("AraBOT", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isAraBot)
            {
                continue;
            }

            Transform head = actor.transform.Find("Head");
            speechAnchor = head != null ? head : actor.transform;
            break;
        }

        return speechAnchor;
    }

    private static void SetGameObjectActive(Component component, bool visible)
    {
        if (component != null)
        {
            component.gameObject.SetActive(visible);
        }
    }

    private static void SetGameObjectActive(GameObject gameObject, bool visible)
    {
        if (gameObject != null)
        {
            gameObject.SetActive(visible);
        }
    }

    private Camera ResolveWorldCamera()
    {
        if (Camera.main != null)
        {
            return Camera.main;
        }

#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<Camera>();
#else
        return FindObjectOfType<Camera>();
#endif
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

        return pressed;
    }

    private T GetOrAddComponent<T>() where T : Component
    {
        T component = GetComponent<T>();
        if (component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        return component;
    }

    private static Button CreateSpriteButton(
        string name,
        Transform parent,
        Sprite sprite,
        Color color,
        Vector2 size,
        out RectTransform rectTransform,
        out Image image)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);

        rectTransform = buttonObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;

        image = buttonObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = false;
        image.color = color;

        Button button = buttonObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        return button;
    }

    private static RectTransform CreateEmptyRect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        GameObject rectObject = new GameObject(name);
        rectObject.transform.SetParent(parent, false);

        RectTransform rectTransform = rectObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;
        return rectTransform;
    }

    private static Image CreateImage(
        string name,
        Transform parent,
        Sprite sprite,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        GameObject imageObject = new GameObject(name);
        imageObject.transform.SetParent(parent, false);

        RectTransform rectTransform = imageObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.preserveAspect = true;
        return image;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        TMP_FontAsset fontAsset,
        string text,
        float fontSize,
        FontStyles fontStyle,
        Color color,
        TextAlignmentOptions alignment,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        TextMeshProUGUI textComponent = textObject.AddComponent<TextMeshProUGUI>();
        textComponent.font = fontAsset;
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.fontStyle = fontStyle;
        textComponent.color = color;
        textComponent.alignment = alignment;
        textComponent.enableWordWrapping = true;
        return textComponent;
    }

    private static TMP_InputField CreateInputField(
        string name,
        Transform parent,
        TMP_FontAsset fontAsset,
        string placeholderText,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        GameObject inputObject = new GameObject(name);
        inputObject.transform.SetParent(parent, false);

        RectTransform rootFieldRect = inputObject.AddComponent<RectTransform>();
        rootFieldRect.anchorMin = anchorMin;
        rootFieldRect.anchorMax = anchorMax;
        rootFieldRect.pivot = new Vector2(0.5f, 0f);
        rootFieldRect.anchoredPosition = anchoredPosition;
        rootFieldRect.sizeDelta = sizeDelta;

        Image background = inputObject.AddComponent<Image>();
        background.color = new Color(0.86f, 0.95f, 0.99f, 1f);

        TMP_InputField inputField = inputObject.AddComponent<TMP_InputField>();
        inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
        inputField.caretColor = new Color(0.08f, 0.35f, 0.49f, 1f);

        GameObject textAreaObject = new GameObject("Text Area");
        textAreaObject.transform.SetParent(inputObject.transform, false);

        RectTransform textAreaRect = textAreaObject.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(16f, 12f);
        textAreaRect.offsetMax = new Vector2(-16f, -12f);
        textAreaObject.AddComponent<RectMask2D>();

        TextMeshProUGUI textComponent = (TextMeshProUGUI)CreateText(
            "Text",
            textAreaRect,
            fontAsset,
            string.Empty,
            22f,
            FontStyles.Normal,
            new Color(0.12f, 0.23f, 0.31f, 1f),
            TextAlignmentOptions.TopLeft,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);

        TextMeshProUGUI placeholderComponent = (TextMeshProUGUI)CreateText(
            "Placeholder",
            textAreaRect,
            fontAsset,
            placeholderText,
            22f,
            FontStyles.Italic,
            new Color(0.12f, 0.23f, 0.31f, 0.42f),
            TextAlignmentOptions.TopLeft,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);

        inputField.textViewport = textAreaRect;
        inputField.textComponent = textComponent;
        inputField.placeholder = placeholderComponent;

        return inputField;
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

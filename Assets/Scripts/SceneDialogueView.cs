using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum DialogueRevealMode
{
    Instant = 0,
    PerWord = 1,
    PerLetter = 2
}

[DisallowMultipleComponent]
public sealed class SceneDialogueView : MonoBehaviour, IDialogueView
{
    private const string DefaultExternalInputPlaceholder = "Type your reply here...";

    [Header("References")]
    [SerializeField] private GameObject dialogueContainer;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private GameObject continueIndicator;
    [SerializeField] private Button backButton;

    [Header("Speaker Styles")]
    [SerializeField] private Image dialogueBoxImage;
    [SerializeField] private Image nameBoxImage;
    [SerializeField] private Image pointingArrowImage;
    [SerializeField] private Sprite defaultDialogueBoxSprite;
    [SerializeField] private Sprite defaultNameBoxSprite;
    [SerializeField] private Sprite defaultPointingArrowSprite;
    [SerializeField] private Sprite studentDialogueBoxSprite;
    [SerializeField] private Sprite studentNameBoxSprite;
    [SerializeField] private Sprite studentPointingArrowSprite;
    [SerializeField] private bool useSpeakerSpecificStyles = true;

    [Header("Panel Juice")]
    [SerializeField] private bool animatePanelAppearance;
    [SerializeField, Min(0.01f)] private float panelAppearDuration = 0.3f;
    [SerializeField, Min(0f)] private float panelAppearRiseDistance = 110f;
    [SerializeField, Range(0.1f, 1f)] private float panelAppearStartScale = 0.88f;
    [SerializeField, Range(1f, 1.2f)] private float panelAppearOvershootScale = 1.035f;

    [Header("Back Button Juice")]
    [SerializeField, Min(0.01f)] private float backButtonAppearDuration = 0.24f;
    [SerializeField, Min(0f)] private float backButtonAppearRiseDistance = 28f;
    [SerializeField, Range(0.1f, 1f)] private float backButtonAppearStartScale = 0.82f;
    [SerializeField, Range(1f, 1.2f)] private float backButtonAppearOvershootScale = 1.04f;

    [Header("Reveal")]
    [SerializeField] private DialogueRevealMode revealMode = DialogueRevealMode.PerLetter;
    [SerializeField, Min(1f)] private float wordsPerSecond = 5f;
    [SerializeField, Min(1f)] private float lettersPerSecond = 24f;
    [SerializeField, Min(0f)] private float punctuationPauseSeconds = 0.18f;
    [SerializeField, Min(0f)] private float whitespacePauseSeconds = 0.1f;
    [SerializeField, Min(1f)] private float liveTranscriptCharactersPerSecond = 42f;

    [Header("Letter Juice")]
    [SerializeField, Min(0.01f)] private float letterSpawnDuration = 0.16f;
    [SerializeField, Min(0f)] private float letterSpawnRiseDistance = 10f;
    [SerializeField, Min(0f)] private float letterSpawnOvershootHeight = 2.5f;
    [SerializeField, Min(0f)] private float letterSpawnScaleBoost = 0.08f;

    [Header("Next Symbol")]
    [SerializeField, Min(0f)] private float continueBounceDistance = 18f;
    [SerializeField, Min(0.1f)] private float continueBounceCyclesPerSecond = 2.4f;

    [Header("External Hints")]
    [SerializeField, Range(0f, 1f)] private float externalHintAlpha = 0.48f;
    [SerializeField] private Color externalHintColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("AraBOT Response Scrolling")]
    [SerializeField, Min(1)] private int responseLinesBeforeScrolling = 3;
    [SerializeField, Min(4f)] private float responseScrollbarWidth = 12f;

    private Coroutine revealRoutine;
    private Coroutine liveTranscriptRoutine;
    private Coroutine panelAppearRoutine;
    private Coroutine backButtonAppearRoutine;
    private RectTransform dialogueContainerRect;
    private RectTransform backButtonRect;
    private Vector2 backButtonBasePosition;
    private Vector3 backButtonBaseScale = Vector3.one;
    private CanvasGroup dialogueCanvasGroup;
    private Vector2 dialogueContainerBasePosition;
    private Vector3 dialogueContainerBaseScale = Vector3.one;
    private float dialogueContainerBaseAlpha = 1f;
    private RectTransform continueIndicatorRect;
    private Vector2 continueIndicatorBasePosition;
    private Color bodyTextBaseColor = Color.white;
    private FontStyles bodyTextBaseFontStyle = FontStyles.Normal;
    private string currentFullText = string.Empty;
    private string liveTranscriptTarget = string.Empty;
    private bool canAdvance;
    private bool hasCompletedDialoguePage;
    private TMP_InputField externalInputField;
    private TMP_Text externalInputText;
    private TMP_Text externalInputPlaceholderText;
    private Button externalBodyActionButton;
    private TMP_MeshInfo[] cachedBodyMeshInfo;
    private ScrollRect responseScrollRect;
    private RectTransform responseViewportRect;
    private RectTransform responseContentRect;
    private GameObject responseScrollbarObject;
    private TextOverflowModes bodyTextBaseOverflowMode;
    private bool isAraBotTurn;
    private bool isNonSpokenContent;
    private bool isResponseScrollable;
    private readonly List<ActiveGlyphAnimation> activeGlyphAnimations = new List<ActiveGlyphAnimation>();

    public static SceneDialogueView ActiveInstance { get; private set; }
    public bool IsRevealComplete { get; private set; } = true;
    public RectTransform DialogueControlsRoot
    {
        get
        {
            if (dialogueContainerRect == null)
            {
                CacheDialogueContainer();
            }

            return dialogueContainerRect;
        }
    }

    public RectTransform DialogueCanvasRoot
    {
        get
        {
            RectTransform controlsRoot = DialogueControlsRoot;
            Canvas dialogueCanvas = controlsRoot != null
                ? controlsRoot.GetComponentInParent<Canvas>()
                : null;
            return dialogueCanvas != null ? dialogueCanvas.transform as RectTransform : null;
        }
    }

    public TMP_InputField ExternalInputField
    {
        get
        {
            EnsureExternalInputField();
            return externalInputField;
        }
    }

    private void Awake()
    {
        ActiveInstance = this;
        CacheDialogueContainer();
        ResolveSpeakerStyleReferences();
        InitializeBackButton();

        if (bodyText != null)
        {
            bodyTextBaseColor = bodyText.color;
            bodyTextBaseFontStyle = bodyText.fontStyle;
            bodyTextBaseOverflowMode = bodyText.overflowMode;
            InitializeResponseScrollView();
        }

        if (continueIndicator != null)
        {
            continueIndicatorRect = continueIndicator.transform as RectTransform;
            if (continueIndicatorRect != null)
            {
                continueIndicatorBasePosition = continueIndicatorRect.anchoredPosition;
            }
        }

        SetVisible(false);
    }

    private void Update()
    {
        UpdateContinueIndicator();
        UpdateActiveGlyphAnimations();
        RefreshBackButton();
    }

    private void OnDestroy()
    {
        if (backButton != null)
        {
            backButton.onClick.RemoveListener(HandleBackPressed);
            StopBackButtonAppearRoutine();
        }

        if (ActiveInstance == this)
        {
            ActiveInstance = null;
        }
    }

    public void SetExternalBodyAction(UnityEngine.Events.UnityAction action)
    {
        if (bodyText == null)
        {
            return;
        }

        if (externalBodyActionButton == null)
        {
            externalBodyActionButton = bodyText.GetComponent<Button>();
            if (externalBodyActionButton == null)
            {
                externalBodyActionButton = bodyText.gameObject.AddComponent<Button>();
            }

            externalBodyActionButton.transition = Selectable.Transition.None;
            externalBodyActionButton.targetGraphic = bodyText;
        }

        externalBodyActionButton.onClick.RemoveAllListeners();
        if (action != null)
        {
            externalBodyActionButton.onClick.AddListener(action);
        }

        externalBodyActionButton.enabled = action != null;
        externalBodyActionButton.interactable = action != null;
        bodyText.raycastTarget = action != null;
    }

    private void UpdateContinueIndicator()
    {
        if (continueIndicatorRect == null || continueIndicator == null || !continueIndicator.activeSelf)
        {
            return;
        }

        float horizontalBounce = Mathf.Abs(
            Mathf.Sin(Time.unscaledTime * continueBounceCyclesPerSecond * Mathf.PI * 2f)) * continueBounceDistance;
        continueIndicatorRect.anchoredPosition = continueIndicatorBasePosition + (Vector2.left * horizontalBounce);
    }

    private void UpdateActiveGlyphAnimations()
    {
        if (bodyText == null || activeGlyphAnimations.Count == 0 || cachedBodyMeshInfo == null)
        {
            return;
        }

        TMP_TextInfo textInfo = bodyText.textInfo;
        if (textInfo.meshInfo == null || textInfo.meshInfo.Length == 0)
        {
            return;
        }

        RestoreBodyTextVertices(textInfo);

        bool hasActiveAnimations = false;
        float now = Time.unscaledTime;
        for (int index = activeGlyphAnimations.Count - 1; index >= 0; index--)
        {
            ActiveGlyphAnimation animation = activeGlyphAnimations[index];
            float progress = Mathf.Clamp01((now - animation.StartTime) / Mathf.Max(letterSpawnDuration, 0.01f));

            ApplyGlyphAnimation(textInfo, animation.CharacterIndex, progress);

            if (progress >= 1f)
            {
                activeGlyphAnimations.RemoveAt(index);
            }
            else
            {
                hasActiveAnimations = true;
            }
        }

        PushBodyTextVertices(textInfo);

        if (!hasActiveAnimations)
        {
            RestoreBodyTextVertices(textInfo);
            PushBodyTextVertices(textInfo);
        }
    }

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            ShowDialogueContainer();
            return;
        }

        StopPanelAppearRoutine();
        RestoreDialogueContainerTransform();
        if (dialogueContainer != null)
        {
            dialogueContainer.SetActive(false);
        }

        StopRevealRoutine();
        StopLiveTranscriptRoutine();
        ClearActiveGlyphAnimations();
        IsRevealComplete = true;
        hasCompletedDialoguePage = false;
        currentFullText = string.Empty;
        canAdvance = false;
        isAraBotTurn = false;
        isNonSpokenContent = false;
        SetExternalBodyAction(null);
        SetExternalInputVisible(false);

        if (speakerText != null)
        {
            speakerText.text = string.Empty;
        }

        if (bodyText != null)
        {
            bodyText.text = string.Empty;
            bodyText.maxVisibleCharacters = 0;
        }

        RefreshResponseScrolling(true);

        SetContinueIndicator(false);
    }

    public void DisplayLine(IDialogueLine line, string visibleText, bool lineCanAdvance)
    {
        ShowDialogueContainer();
        ApplySpeakerStyle(UsesBrownSpeakerStyle(line));
        StopLiveTranscriptRoutine();

        // The voice player normally receives LineChanged before the view does,
        // but it can be bootstrapped later when entering a scene. Resolve it
        // here too so generated text never starts revealing before its speech
        // request has registered.
        DialogueVoicePlayer voicePlayer = DialogueVoicePlayer.GetOrCreate();
        bool waitingForVoice = voicePlayer != null && voicePlayer.IsPreparingLine(line);
        isAraBotTurn = IsAraBotSpeaker(line);
        isNonSpokenContent = waitingForVoice;

        SetExternalBodyAction(null);
        SetExternalInputVisible(false);
        RestoreBodyTextAppearance();

        if (speakerText != null)
        {
            speakerText.text = line != null ? line.SpeakerName : string.Empty;
        }

        currentFullText = visibleText ?? string.Empty;
        canAdvance = lineCanAdvance;
        hasCompletedDialoguePage = false;
        SetContinueIndicator(false);
        StopRevealRoutine();
        ClearActiveGlyphAnimations();

        if (bodyText == null)
        {
            IsRevealComplete = true;
            return;
        }

        if (string.IsNullOrEmpty(currentFullText))
        {
            bodyText.text = currentFullText;
            bodyText.maxVisibleCharacters = int.MaxValue;
            CacheBodyTextMesh();
            IsRevealComplete = true;
            hasCompletedDialoguePage = true;
            RefreshResponseScrolling(true);
            SetContinueIndicator(canAdvance);
            RefreshBackButton();
            return;
        }

        bodyText.text = waitingForVoice ? GetThinkingMessage(line) : currentFullText;
        bodyText.maxVisibleCharacters = waitingForVoice ? int.MaxValue : 0;
        if (waitingForVoice)
        {
            ApplyExternalHintAppearance();
        }
        CacheBodyTextMesh();
        RefreshResponseScrolling(true);
        IsRevealComplete = false;
        revealRoutine = StartCoroutine(RevealRoutine(line));
        RefreshBackButton();
    }

    public void ShowExternalContent(
        string speaker,
        string body,
        bool lineCanAdvance,
        bool useStudentStyle = false)
    {
        ShowDialogueContainer();
        ApplySpeakerStyle(useStudentStyle);

        StopRevealRoutine();
        StopLiveTranscriptRoutine();
        ClearActiveGlyphAnimations();
        SetExternalBodyAction(null);
        SetExternalInputVisible(false);
        RestoreBodyTextAppearance();

        currentFullText = body ?? string.Empty;
        canAdvance = lineCanAdvance;
        IsRevealComplete = true;
        hasCompletedDialoguePage = true;
        isAraBotTurn = IsAraBotSpeakerName(speaker);
        isNonSpokenContent = false;

        if (speakerText != null)
        {
            speakerText.text = speaker ?? string.Empty;
        }

        if (bodyText != null)
        {
            bodyText.gameObject.SetActive(true);
            bodyText.text = currentFullText;
            bodyText.maxVisibleCharacters = int.MaxValue;
            CacheBodyTextMesh();
        }

        RefreshResponseScrolling(true);

        SetContinueIndicator(canAdvance);
        RefreshBackButton();
    }

    public void ShowExternalContentSmooth(
        string speaker,
        string body,
        bool lineCanAdvance,
        bool useStudentStyle = false)
    {
        ShowDialogueContainer();
        ApplySpeakerStyle(useStudentStyle);

        if (speakerText != null)
        {
            speakerText.text = speaker ?? string.Empty;
        }

        isAraBotTurn = IsAraBotSpeakerName(speaker);
        isNonSpokenContent = false;
        hasCompletedDialoguePage = true;
        SetExternalBodyTextSmooth(body, lineCanAdvance);
        RefreshBackButton();
    }

    public void ShowExternalHint(
        string speaker,
        string hint,
        bool canAdvance = false,
        bool useStudentStyle = false)
    {
        ShowExternalContent(speaker, hint, canAdvance, useStudentStyle);
        isNonSpokenContent = true;

        if (bodyText != null)
        {
            ApplyExternalHintAppearance();
        }

        RefreshResponseScrolling(true);
    }

    private void CacheDialogueContainer()
    {
        if (dialogueContainer == null)
        {
            return;
        }

        dialogueContainerRect = dialogueContainer.transform as RectTransform;
        dialogueCanvasGroup = dialogueContainer.GetComponent<CanvasGroup>();

        if (dialogueContainerRect != null)
        {
            dialogueContainerBasePosition = dialogueContainerRect.anchoredPosition;
            dialogueContainerBaseScale = dialogueContainerRect.localScale;
        }

        if (dialogueCanvasGroup != null)
        {
            dialogueContainerBaseAlpha = dialogueCanvasGroup.alpha;
        }
    }

    private void InitializeBackButton()
    {
        if (dialogueContainer == null)
        {
            return;
        }

        if (backButton == null)
        {
            Transform existingButton = dialogueContainer.transform.Find("Dialogue Back Button");
            if (existingButton == null)
            {
                existingButton = dialogueContainer.transform.Find("Back Button");
            }

            backButton = existingButton != null ? existingButton.GetComponent<Button>() : null;
        }

        if (backButton == null)
        {
            backButton = CreateRuntimeBackButton();
        }

        if (backButton == null)
        {
            return;
        }

        if (backButton.targetGraphic == null)
        {
            backButton.targetGraphic = backButton.GetComponent<Image>();
        }

        backButton.onClick.RemoveListener(HandleBackPressed);
        backButton.onClick.AddListener(HandleBackPressed);
        backButtonRect = backButton.transform as RectTransform;
        if (backButtonRect != null)
        {
            backButtonBasePosition = backButtonRect.anchoredPosition;
            backButtonBaseScale = backButtonRect.localScale;
        }
        backButton.gameObject.SetActive(false);
    }

    private Button CreateRuntimeBackButton()
    {
        if (dialogueContainer == null)
        {
            return null;
        }

        GameObject buttonObject = new GameObject(
            "Dialogue Back Button",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.layer = dialogueContainer.layer;

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.SetParent(dialogueContainer.transform, false);
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.zero;
        buttonRect.pivot = Vector2.zero;
        buttonRect.anchoredPosition = new Vector2(150f, 70f);
        buttonRect.sizeDelta = new Vector2(130f, 100f);

        Image buttonImage = buttonObject.GetComponent<Image>();
        DialogueUiIconLibrary icons = DialogueUiIconLibrary.Load();
        buttonImage.sprite = icons != null ? icons.BackIcon : null;
        buttonImage.type = Image.Type.Simple;
        buttonImage.preserveAspect = true;
        buttonImage.color = Color.white;

        Button createdButton = buttonObject.GetComponent<Button>();
        createdButton.targetGraphic = buttonImage;
        return createdButton;
    }

    private void RefreshBackButton()
    {
        if (backButton == null)
        {
            return;
        }

        DialogueManager manager = DialogueManager.Instance;
        bool canGoBack = manager != null && manager.CanGoBack;
        bool canRevisitQuestion = StudentDialogueInteraction.CanRevisitActiveQuestion();
        bool shouldShow = dialogueContainer != null
            && dialogueContainer.activeInHierarchy
            && isAraBotTurn
            && hasCompletedDialoguePage
            && (canGoBack || canRevisitQuestion);

        if (shouldShow && !backButton.gameObject.activeSelf)
        {
            ShowBackButton();
        }
        else if (!shouldShow && backButton.gameObject.activeSelf)
        {
            HideBackButton();
        }
    }

    private void ShowBackButton()
    {
        backButton.gameObject.SetActive(true);
        RestoreBackButtonTransform();

        if (backButtonRect == null)
        {
            return;
        }

        StopBackButtonAppearRoutine();
        backButtonAppearRoutine = StartCoroutine(AnimateBackButtonIn());
    }

    private void HideBackButton()
    {
        StopBackButtonAppearRoutine();
        RestoreBackButtonTransform();
        backButton.gameObject.SetActive(false);
    }

    private IEnumerator AnimateBackButtonIn()
    {
        Vector2 startPosition = backButtonBasePosition + (Vector2.down * backButtonAppearRiseDistance);
        float elapsed = 0f;

        while (elapsed < backButtonAppearDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(backButtonAppearDuration, 0.01f));
            float easedPosition = 1f - Mathf.Pow(1f - progress, 3f);
            backButtonRect.anchoredPosition = Vector2.LerpUnclamped(
                startPosition,
                backButtonBasePosition,
                easedPosition);

            float scale = EvaluateBackButtonAppearScale(progress);
            backButtonRect.localScale = new Vector3(
                backButtonBaseScale.x * scale,
                backButtonBaseScale.y * scale,
                backButtonBaseScale.z);
            yield return null;
        }

        RestoreBackButtonTransform();
        backButtonAppearRoutine = null;
    }

    private float EvaluateBackButtonAppearScale(float progress)
    {
        const float overshootPoint = 0.72f;
        if (progress < overshootPoint)
        {
            float riseProgress = Mathf.SmoothStep(0f, 1f, progress / overshootPoint);
            return Mathf.LerpUnclamped(backButtonAppearStartScale, backButtonAppearOvershootScale, riseProgress);
        }

        float settleProgress = Mathf.SmoothStep(0f, 1f, (progress - overshootPoint) / (1f - overshootPoint));
        return Mathf.LerpUnclamped(backButtonAppearOvershootScale, 1f, settleProgress);
    }

    private void StopBackButtonAppearRoutine()
    {
        if (backButtonAppearRoutine == null)
        {
            return;
        }

        StopCoroutine(backButtonAppearRoutine);
        backButtonAppearRoutine = null;
    }

    private void RestoreBackButtonTransform()
    {
        if (backButtonRect == null)
        {
            return;
        }

        backButtonRect.anchoredPosition = backButtonBasePosition;
        backButtonRect.localScale = backButtonBaseScale;
    }

    private void HandleBackPressed()
    {
        if (!isAraBotTurn)
        {
            return;
        }

        DialogueManager manager = DialogueManager.Instance;
        if (manager != null && manager.CanGoBack)
        {
            manager.GoBack();
            return;
        }

        StudentDialogueInteraction.RevisitActiveQuestion();
    }

    public bool IsPointerOverControlButton()
    {
        if (!TryGetPointerPosition(out Vector2 pointerPosition))
        {
            return false;
        }

        if (dialogueContainer != null)
        {
            Button[] buttons = dialogueContainer.GetComponentsInChildren<Button>(false);
            for (int index = 0; index < buttons.Length; index++)
            {
                RectTransform buttonRect = buttons[index] != null
                    ? buttons[index].transform as RectTransform
                    : null;
                if (buttonRect != null
                    && RectTransformUtility.RectangleContainsScreenPoint(buttonRect, pointerPosition))
                {
                    return true;
                }
            }
        }

        if (isResponseScrollable
            && responseViewportRect != null
            && RectTransformUtility.RectangleContainsScreenPoint(responseViewportRect, pointerPosition))
        {
            return true;
        }

        return PauseMenuController.IsPointerOverPauseButton(pointerPosition);
    }

    private static bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            pointerPosition = Mouse.current.position.ReadValue();
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        pointerPosition = Input.mousePosition;
        return true;
#else
        pointerPosition = Vector2.zero;
        return false;
#endif
    }

    private void ShowDialogueContainer()
    {
        if (dialogueContainer == null)
        {
            return;
        }

        bool shouldAnimate = !dialogueContainer.activeSelf;
        dialogueContainer.SetActive(true);

        if (!shouldAnimate || !animatePanelAppearance)
        {
            RestoreDialogueContainerTransform();
            return;
        }

        StopPanelAppearRoutine();
        panelAppearRoutine = StartCoroutine(AnimateDialogueContainerIn());
    }

    private IEnumerator AnimateDialogueContainerIn()
    {
        if (dialogueContainerRect == null)
        {
            panelAppearRoutine = null;
            yield break;
        }

        Vector2 startPosition = dialogueContainerBasePosition + (Vector2.down * panelAppearRiseDistance);
        float elapsed = 0f;

        while (elapsed < panelAppearDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(panelAppearDuration, 0.01f));
            float easedPosition = 1f - Mathf.Pow(1f - progress, 3f);

            dialogueContainerRect.anchoredPosition = Vector2.LerpUnclamped(
                startPosition,
                dialogueContainerBasePosition,
                easedPosition);

            float scale = EvaluatePanelAppearScale(progress);
            dialogueContainerRect.localScale = new Vector3(
                dialogueContainerBaseScale.x * scale,
                dialogueContainerBaseScale.y * scale,
                dialogueContainerBaseScale.z);

            if (dialogueCanvasGroup != null)
            {
                dialogueCanvasGroup.alpha = dialogueContainerBaseAlpha * easedPosition;
            }

            yield return null;
        }

        RestoreDialogueContainerTransform();
        panelAppearRoutine = null;
    }

    private float EvaluatePanelAppearScale(float progress)
    {
        const float overshootPoint = 0.72f;
        if (progress < overshootPoint)
        {
            float riseProgress = Mathf.SmoothStep(0f, 1f, progress / overshootPoint);
            return Mathf.LerpUnclamped(panelAppearStartScale, panelAppearOvershootScale, riseProgress);
        }

        float settleProgress = Mathf.SmoothStep(0f, 1f, (progress - overshootPoint) / (1f - overshootPoint));
        return Mathf.LerpUnclamped(panelAppearOvershootScale, 1f, settleProgress);
    }

    private void StopPanelAppearRoutine()
    {
        if (panelAppearRoutine == null)
        {
            return;
        }

        StopCoroutine(panelAppearRoutine);
        panelAppearRoutine = null;
    }

    private void RestoreDialogueContainerTransform()
    {
        if (dialogueContainerRect != null)
        {
            dialogueContainerRect.anchoredPosition = dialogueContainerBasePosition;
            dialogueContainerRect.localScale = dialogueContainerBaseScale;
        }

        if (dialogueCanvasGroup != null)
        {
            dialogueCanvasGroup.alpha = dialogueContainerBaseAlpha;
        }
    }

    private void RestoreBodyTextAppearance()
    {
        if (bodyText != null)
        {
            bodyText.color = bodyTextBaseColor;
            bodyText.fontStyle = bodyTextBaseFontStyle;
        }
    }

    private void ApplyExternalHintAppearance()
    {
        if (bodyText == null)
        {
            return;
        }

        bodyText.color = new Color(
            externalHintColor.r,
            externalHintColor.g,
            externalHintColor.b,
            bodyTextBaseColor.a * externalHintAlpha);
        bodyText.fontStyle = bodyTextBaseFontStyle | FontStyles.Italic;
    }

    private void ResolveSpeakerStyleReferences()
    {
        if (dialogueBoxImage == null && bodyText != null)
        {
            dialogueBoxImage = bodyText.GetComponentInParent<Image>();
        }

        if (nameBoxImage == null && speakerText != null)
        {
            nameBoxImage = speakerText.GetComponentInParent<Image>();
        }

        if (defaultDialogueBoxSprite == null && dialogueBoxImage != null)
        {
            defaultDialogueBoxSprite = dialogueBoxImage.sprite;
        }

        if (defaultNameBoxSprite == null && nameBoxImage != null)
        {
            defaultNameBoxSprite = nameBoxImage.sprite;
        }
    }

    private void ApplySpeakerStyle(bool useStudentStyle)
    {
        if (!useSpeakerSpecificStyles)
        {
            return;
        }

        ApplySlicedSprite(dialogueBoxImage, useStudentStyle ? studentDialogueBoxSprite : defaultDialogueBoxSprite);
        ApplySlicedSprite(nameBoxImage, useStudentStyle ? studentNameBoxSprite : defaultNameBoxSprite);
        ApplySlicedSprite(pointingArrowImage, useStudentStyle ? studentPointingArrowSprite : defaultPointingArrowSprite);
    }

    private static void ApplySlicedSprite(Image image, Sprite sprite)
    {
        if (image == null || sprite == null)
        {
            return;
        }

        image.sprite = sprite;
        image.type = Image.Type.Sliced;
    }

    private static bool UsesBrownSpeakerStyle(IDialogueLine line)
    {
        if (line == null || line.SpeakerReference == null)
        {
            return false;
        }

        if (line.SpeakerReference is StudentPersonality)
        {
            return true;
        }

        if (line.SpeakerReference is Component component)
        {
            DialogueActor actor = component.GetComponentInParent<DialogueActor>();
            return (actor != null && actor.UsesBrownDialogueStyle)
                || component.GetComponentInParent<StudentPersonality>() != null;
        }

        if (line.SpeakerReference is GameObject gameObject)
        {
            DialogueActor actor = gameObject.GetComponentInParent<DialogueActor>();
            return (actor != null && actor.UsesBrownDialogueStyle)
                || gameObject.GetComponentInParent<StudentPersonality>() != null;
        }

        return false;
    }

    private static bool IsAraBotSpeaker(IDialogueLine line)
    {
        return line != null && IsAraBotSpeakerName(line.SpeakerName);
    }

    private static bool IsAraBotSpeakerName(string speakerName)
    {
        return !string.IsNullOrWhiteSpace(speakerName)
            && speakerName.IndexOf("arabot", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void SetExternalBodyText(string body, bool lineCanAdvance)
    {
        ShowExternalContent(speakerText != null ? speakerText.text : string.Empty, body, lineCanAdvance);
    }

    /// <summary>
    /// Updates live speech-recognition text without resetting the dialogue panel
    /// every frame. New words ease in while recognition corrections replace only
    /// the changed suffix.
    /// </summary>
    public void SetExternalBodyTextSmooth(string body, bool lineCanAdvance)
    {
        ShowDialogueContainer();
        StopRevealRoutine();
        SetExternalBodyAction(null);
        SetExternalInputVisible(false);
        RestoreBodyTextAppearance();

        currentFullText = body ?? string.Empty;
        liveTranscriptTarget = currentFullText;
        canAdvance = lineCanAdvance;
        IsRevealComplete = true;
        hasCompletedDialoguePage = true;
        isNonSpokenContent = false;
        SetContinueIndicator(canAdvance);

        if (bodyText == null)
        {
            return;
        }

        bodyText.gameObject.SetActive(true);
        if (liveTranscriptRoutine == null)
        {
            liveTranscriptRoutine = StartCoroutine(RevealLiveTranscriptRoutine());
        }

        RefreshResponseScrolling();
    }

    public void SetExternalInputVisible(bool visible, string placeholder = null, string currentValue = null)
    {
        if (visible)
        {
            EnsureExternalInputField();
        }

        if (bodyText != null)
        {
            bodyText.gameObject.SetActive(!visible);
        }

        if (externalInputField == null)
        {
            return;
        }

        externalInputField.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        if (externalInputPlaceholderText != null)
        {
            externalInputPlaceholderText.text = string.IsNullOrWhiteSpace(placeholder)
                ? DefaultExternalInputPlaceholder
                : placeholder;
        }

        if (currentValue != null)
        {
            externalInputField.text = currentValue;
        }
    }

    public void FocusExternalInputField()
    {
        if (ExternalInputField == null)
        {
            return;
        }

        externalInputField.ActivateInputField();
        externalInputField.Select();
    }

    public void CompleteReveal()
    {
        StopRevealRoutine();
        ClearActiveGlyphAnimations();
        IsRevealComplete = true;
        hasCompletedDialoguePage = true;

        if (bodyText != null)
        {
            bodyText.text = currentFullText;
            bodyText.maxVisibleCharacters = int.MaxValue;
            CacheBodyTextMesh();
        }

        isNonSpokenContent = false;
        RestoreBodyTextAppearance();
        RefreshResponseScrolling();

        SetContinueIndicator(canAdvance);
    }

    private IEnumerator RevealRoutine(IDialogueLine line)
    {
        DialogueVoicePlayer voicePlayer = DialogueVoicePlayer.GetOrCreate();
        bool waitingForVoice = voicePlayer != null && voicePlayer.IsPreparingLine(line);

        while (voicePlayer != null && voicePlayer.IsPreparingLine(line))
        {
            yield return null;
        }

        if (waitingForVoice && bodyText != null)
        {
            isNonSpokenContent = false;
            RestoreBodyTextAppearance();
            bodyText.text = currentFullText;
            bodyText.maxVisibleCharacters = 0;
            CacheBodyTextMesh();
            RefreshResponseScrolling(true);
        }

        // Let the voice player start the clip before reading its duration. This
        // is normally one frame and avoids a visual head start on cached clips.
        yield return null;

        if (revealMode == DialogueRevealMode.Instant)
        {
            CompleteReveal();
            yield break;
        }

        List<RevealChunk> chunks = BuildRevealChunks(bodyText.textInfo, revealMode);
        if (chunks.Count == 0)
        {
            CompleteReveal();
            yield break;
        }

        int visibleCharacterCount = 0;
        float delayScale = GetVoiceRevealDelayScale(voicePlayer, line, chunks);

        for (int index = 0; index < chunks.Count; index++)
        {
            RevealChunk chunk = chunks[index];
            int chunkStartCharacterIndex = visibleCharacterCount;
            visibleCharacterCount += chunk.CharacterCount;
            bodyText.maxVisibleCharacters = visibleCharacterCount;
            CacheBodyTextMesh();
            QueueGlyphAnimations(chunk, chunkStartCharacterIndex);
            NotifyVoiceTicks(chunkStartCharacterIndex, chunk.CharacterCount);
            UpdateActiveGlyphAnimations();

            if (index < chunks.Count - 1)
            {
                yield return WaitForSecondsRealtime(GetChunkDelaySeconds(chunk) * delayScale);
            }
        }

        revealRoutine = null;
        IsRevealComplete = true;
        hasCompletedDialoguePage = true;
        bodyText.maxVisibleCharacters = int.MaxValue;
        CacheBodyTextMesh();
        RefreshResponseScrolling();
        SetContinueIndicator(canAdvance);
    }

    private static string GetThinkingMessage(IDialogueLine line)
    {
        string speakerName = line != null ? line.SpeakerName : string.Empty;
        return string.IsNullOrWhiteSpace(speakerName)
            ? "Student is thinking..."
            : speakerName.Trim() + " is thinking...";
    }

    private void SetContinueIndicator(bool visible)
    {
        if (continueIndicator == null)
        {
            return;
        }

        continueIndicator.SetActive(visible);
        if (visible && continueIndicatorRect != null)
        {
            continueIndicatorRect.anchoredPosition = continueIndicatorBasePosition;
        }
    }

    private void StopRevealRoutine()
    {
        if (revealRoutine == null)
        {
            return;
        }

        StopCoroutine(revealRoutine);
        revealRoutine = null;
    }

    private void StopLiveTranscriptRoutine()
    {
        if (liveTranscriptRoutine == null)
        {
            return;
        }

        StopCoroutine(liveTranscriptRoutine);
        liveTranscriptRoutine = null;
        liveTranscriptTarget = string.Empty;
    }

    private IEnumerator RevealLiveTranscriptRoutine()
    {
        string displayedText = bodyText != null ? bodyText.text : string.Empty;
        float secondsPerCharacter = 1f / Mathf.Max(1f, liveTranscriptCharactersPerSecond);

        while (bodyText != null)
        {
            string target = liveTranscriptTarget ?? string.Empty;
            int sharedLength = GetSharedPrefixLength(displayedText, target);
            if (sharedLength < displayedText.Length)
            {
                ClearActiveGlyphAnimations();
                displayedText = displayedText.Substring(0, sharedLength);
                bodyText.text = displayedText;
                bodyText.maxVisibleCharacters = int.MaxValue;
                CacheBodyTextMesh();
                RefreshResponseScrolling();
            }

            if (displayedText.Length < target.Length)
            {
                displayedText += target[displayedText.Length];
                bodyText.text = displayedText;
                bodyText.maxVisibleCharacters = int.MaxValue;
                CacheBodyTextMesh();
                QueueLatestVisibleGlyphAnimation();
                RefreshResponseScrolling();
                UpdateActiveGlyphAnimations();
                yield return WaitForSecondsRealtime(secondsPerCharacter);
                continue;
            }

            yield return null;
        }

        liveTranscriptRoutine = null;
    }

    private void QueueLatestVisibleGlyphAnimation()
    {
        if (bodyText == null || bodyText.textInfo == null || bodyText.textInfo.characterCount <= 0)
        {
            return;
        }

        int characterIndex = bodyText.textInfo.characterCount - 1;
        char character = bodyText.textInfo.characterInfo[characterIndex].character;
        if (char.IsWhiteSpace(character))
        {
            return;
        }

        activeGlyphAnimations.Add(new ActiveGlyphAnimation(characterIndex, Time.unscaledTime));
    }

    private float GetVoiceRevealDelayScale(
        DialogueVoicePlayer voicePlayer,
        IDialogueLine line,
        List<RevealChunk> chunks)
    {
        if (voicePlayer == null || !voicePlayer.TryGetLineDuration(line, out float voiceDuration))
        {
            return 1f;
        }

        float defaultRevealDuration = 0f;
        for (int index = 0; index < chunks.Count - 1; index++)
        {
            defaultRevealDuration += GetChunkDelaySeconds(chunks[index]);
        }

        return defaultRevealDuration > 0.01f
            ? Mathf.Clamp(voiceDuration / defaultRevealDuration, 0.2f, 4f)
            : 1f;
    }

    private static int GetSharedPrefixLength(string left, string right)
    {
        int maxLength = Mathf.Min(left.Length, right.Length);
        int index = 0;
        while (index < maxLength && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private void InitializeResponseScrollView()
    {
        if (bodyText == null || responseScrollRect != null)
        {
            return;
        }

        RectTransform bodyRect = bodyText.rectTransform;
        if (bodyRect == null || bodyRect.parent == null)
        {
            return;
        }

        Transform originalParent = bodyRect.parent;
        int originalSiblingIndex = bodyRect.GetSiblingIndex();

        GameObject viewportObject = new GameObject(
            "AraBOT Response Viewport",
            typeof(RectTransform),
            typeof(RectMask2D),
            typeof(ScrollRect));
        viewportObject.layer = bodyText.gameObject.layer;

        responseViewportRect = viewportObject.GetComponent<RectTransform>();
        responseViewportRect.SetParent(originalParent, false);
        responseViewportRect.SetSiblingIndex(originalSiblingIndex);
        responseViewportRect.anchorMin = bodyRect.anchorMin;
        responseViewportRect.anchorMax = bodyRect.anchorMax;
        responseViewportRect.pivot = bodyRect.pivot;
        responseViewportRect.anchoredPosition = bodyRect.anchoredPosition;
        responseViewportRect.sizeDelta = bodyRect.sizeDelta;
        responseViewportRect.localRotation = bodyRect.localRotation;
        responseViewportRect.localScale = bodyRect.localScale;

        bodyRect.SetParent(responseViewportRect, false);
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = Vector2.zero;
        bodyRect.sizeDelta = Vector2.zero;
        bodyRect.localRotation = Quaternion.identity;
        bodyRect.localScale = Vector3.one;
        responseContentRect = bodyRect;

        responseScrollRect = viewportObject.GetComponent<ScrollRect>();
        responseScrollRect.content = responseContentRect;
        responseScrollRect.viewport = responseViewportRect;
        responseScrollRect.horizontal = false;
        responseScrollRect.vertical = true;
        responseScrollRect.movementType = ScrollRect.MovementType.Clamped;
        responseScrollRect.inertia = true;
        responseScrollRect.decelerationRate = 0.12f;
        responseScrollRect.scrollSensitivity = 28f;

        Scrollbar scrollbar = CreateResponseScrollbar(responseViewportRect);
        if (scrollbar != null)
        {
            responseScrollbarObject = scrollbar.gameObject;
            responseScrollRect.verticalScrollbar = scrollbar;
            responseScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            responseScrollRect.verticalScrollbarSpacing = 4f;
        }

        RefreshResponseScrolling(true);
    }

    private Scrollbar CreateResponseScrollbar(RectTransform parent)
    {
        if (parent == null)
        {
            return null;
        }

        GameObject scrollbarObject = new GameObject(
            "AraBOT Response Scrollbar",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Scrollbar));
        scrollbarObject.layer = bodyText.gameObject.layer;
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.SetParent(parent, false);
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.anchoredPosition = Vector2.zero;
        scrollbarRect.sizeDelta = new Vector2(responseScrollbarWidth, 0f);

        Image background = scrollbarObject.GetComponent<Image>();
        background.color = new Color(bodyTextBaseColor.r, bodyTextBaseColor.g, bodyTextBaseColor.b, 0.18f);

        GameObject handleObject = new GameObject(
            "Handle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        handleObject.layer = bodyText.gameObject.layer;
        RectTransform handleRect = handleObject.GetComponent<RectTransform>();
        handleRect.SetParent(scrollbarRect, false);
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = new Vector2(2f, 2f);
        handleRect.offsetMax = new Vector2(-2f, -2f);

        Image handleImage = handleObject.GetComponent<Image>();
        handleImage.color = new Color(bodyTextBaseColor.r, bodyTextBaseColor.g, bodyTextBaseColor.b, 0.72f);

        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.value = 1f;
        scrollbarObject.SetActive(false);
        return scrollbar;
    }

    private void RefreshResponseScrolling(bool resetPosition = false)
    {
        if (bodyText == null
            || responseScrollRect == null
            || responseViewportRect == null
            || responseContentRect == null)
        {
            return;
        }

        bodyText.overflowMode = bodyTextBaseOverflowMode;
        bodyText.ForceMeshUpdate();
        bool shouldScroll = isAraBotTurn
            && !isNonSpokenContent
            && !string.IsNullOrWhiteSpace(bodyText.text)
            && bodyText.textInfo != null
            && bodyText.textInfo.lineCount > Mathf.Max(1, responseLinesBeforeScrolling);
        bool justEnabled = shouldScroll && !isResponseScrollable;

        if (shouldScroll)
        {
            responseContentRect.anchorMin = new Vector2(0f, 1f);
            responseContentRect.anchorMax = Vector2.one;
            responseContentRect.pivot = new Vector2(0.5f, 1f);
            responseContentRect.anchoredPosition = Vector2.zero;
            float viewportHeight = Mathf.Max(1f, responseViewportRect.rect.height);
            float contentHeight = Mathf.Max(viewportHeight, bodyText.preferredHeight + 4f);
            responseContentRect.sizeDelta = new Vector2(0f, contentHeight);
        }
        else
        {
            responseContentRect.anchorMin = Vector2.zero;
            responseContentRect.anchorMax = Vector2.one;
            responseContentRect.pivot = new Vector2(0.5f, 1f);
            responseContentRect.anchoredPosition = Vector2.zero;
            responseContentRect.sizeDelta = Vector2.zero;
        }

        isResponseScrollable = shouldScroll;
        responseScrollRect.vertical = shouldScroll;
        responseScrollRect.enabled = shouldScroll;
        if (responseScrollbarObject != null)
        {
            responseScrollbarObject.SetActive(shouldScroll);
        }

        if (!shouldScroll || resetPosition || justEnabled)
        {
            responseScrollRect.verticalNormalizedPosition = 1f;
        }

        CacheBodyTextMesh();
    }

    private void EnsureExternalInputField()
    {
        if (externalInputField != null || bodyText == null)
        {
            return;
        }

        RectTransform bodyRect = bodyText.rectTransform;
        if (bodyRect == null || bodyRect.parent == null)
        {
            return;
        }

        GameObject inputRoot = new GameObject("Dialogue External Input");
        inputRoot.transform.SetParent(bodyRect.parent, false);

        RectTransform inputRect = inputRoot.AddComponent<RectTransform>();
        inputRect.anchorMin = bodyRect.anchorMin;
        inputRect.anchorMax = bodyRect.anchorMax;
        inputRect.pivot = bodyRect.pivot;
        inputRect.anchoredPosition = bodyRect.anchoredPosition;
        inputRect.sizeDelta = bodyRect.sizeDelta;

        Image inputBackground = inputRoot.AddComponent<Image>();
        inputBackground.color = new Color(0.91f, 0.97f, 1f, 0.18f);

        externalInputField = inputRoot.AddComponent<TMP_InputField>();
        externalInputField.lineType = TMP_InputField.LineType.SingleLine;
        externalInputField.caretColor = bodyText.color;

        GameObject textAreaObject = new GameObject("Text Area");
        textAreaObject.transform.SetParent(inputRoot.transform, false);

        RectTransform textAreaRect = textAreaObject.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(14f, 12f);
        textAreaRect.offsetMax = new Vector2(-14f, -12f);
        textAreaObject.AddComponent<RectMask2D>();

        externalInputText = CreateRuntimeText(
            "Text",
            textAreaRect,
            bodyText.font,
            string.Empty,
            bodyText.fontSize,
            bodyText.fontStyle,
            bodyText.color,
            bodyText.alignment);

        externalInputPlaceholderText = CreateRuntimeText(
            "Placeholder",
            textAreaRect,
            bodyText.font,
            DefaultExternalInputPlaceholder,
            bodyText.fontSize,
            FontStyles.Italic,
            new Color(bodyText.color.r, bodyText.color.g, bodyText.color.b, 0.4f),
            bodyText.alignment);

        externalInputField.textViewport = textAreaRect;
        externalInputField.textComponent = externalInputText;
        externalInputField.placeholder = externalInputPlaceholderText;
        inputRoot.SetActive(false);
    }

    private static TMP_Text CreateRuntimeText(
        string objectName,
        Transform parent,
        TMP_FontAsset fontAsset,
        string text,
        float fontSize,
        FontStyles fontStyle,
        Color color,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

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

    private void ClearActiveGlyphAnimations()
    {
        activeGlyphAnimations.Clear();
        cachedBodyMeshInfo = null;
    }

    private static List<RevealChunk> BuildRevealChunks(TMP_TextInfo textInfo, DialogueRevealMode mode)
    {
        List<RevealChunk> chunks = new List<RevealChunk>();
        if (textInfo == null || textInfo.characterCount <= 0)
        {
            return chunks;
        }

        int index = 0;
        while (index < textInfo.characterCount)
        {
            char current = textInfo.characterInfo[index].character;
            if (char.IsWhiteSpace(current))
            {
                chunks.Add(new RevealChunk(1, RevealChunkKind.Whitespace));
                index++;
                continue;
            }

            if (char.IsLetterOrDigit(current))
            {
                int start = index;
                index++;

                while (index < textInfo.characterCount && IsWordCharacter(textInfo, index))
                {
                    index++;
                }

                int wordCharacterCount = index - start;
                if (mode == DialogueRevealMode.PerLetter)
                {
                    for (int letterIndex = 0; letterIndex < wordCharacterCount; letterIndex++)
                    {
                        chunks.Add(new RevealChunk(1, RevealChunkKind.Text));
                    }
                }
                else
                {
                    chunks.Add(new RevealChunk(wordCharacterCount, RevealChunkKind.Text));
                }

                continue;
            }

            chunks.Add(new RevealChunk(1, RevealChunkKind.Punctuation));
            index++;
        }

        return chunks;
    }

    private static bool IsWordCharacter(TMP_TextInfo textInfo, int index)
    {
        char current = textInfo.characterInfo[index].character;
        if (char.IsLetterOrDigit(current))
        {
            return true;
        }

        if ((current == '\'' || current == '-') &&
            index > 0 &&
            index + 1 < textInfo.characterCount &&
            char.IsLetterOrDigit(textInfo.characterInfo[index - 1].character) &&
            char.IsLetterOrDigit(textInfo.characterInfo[index + 1].character))
        {
            return true;
        }

        return false;
    }

    private float GetChunkDelaySeconds(RevealChunk chunk)
    {
        switch (chunk.Kind)
        {
            case RevealChunkKind.Punctuation:
                return punctuationPauseSeconds;
            case RevealChunkKind.Whitespace:
                return whitespacePauseSeconds;
            default:
                return revealMode == DialogueRevealMode.PerLetter
                    ? 1f / Mathf.Max(lettersPerSecond, 1f)
                    : 1f / Mathf.Max(wordsPerSecond, 1f);
        }
    }

    private void QueueGlyphAnimations(RevealChunk chunk, int startCharacterIndex)
    {
        if (chunk.Kind == RevealChunkKind.Whitespace)
        {
            return;
        }

        float startTime = Time.unscaledTime;
        for (int offset = 0; offset < chunk.CharacterCount; offset++)
        {
            activeGlyphAnimations.Add(new ActiveGlyphAnimation(startCharacterIndex + offset, startTime));
        }
    }

    private void NotifyVoiceTicks(int startCharacterIndex, int characterCount)
    {
        DialogueManager dialogueManager = DialogueManager.Instance;
        if (dialogueManager == null || bodyText == null)
        {
            return;
        }

        TMP_TextInfo textInfo = bodyText.textInfo;
        if (textInfo == null || textInfo.characterCount <= 0)
        {
            return;
        }

        int maxCharacterIndex = Mathf.Min(startCharacterIndex + characterCount, textInfo.characterCount);
        for (int characterIndex = startCharacterIndex; characterIndex < maxCharacterIndex; characterIndex++)
        {
            dialogueManager.NotifyCharacterRevealed(textInfo.characterInfo[characterIndex].character);
        }
    }

    private void CacheBodyTextMesh()
    {
        if (bodyText == null)
        {
            cachedBodyMeshInfo = null;
            return;
        }

        bodyText.ForceMeshUpdate();
        TMP_TextInfo textInfo = bodyText.textInfo;
        if (textInfo == null || textInfo.characterCount <= 0 || textInfo.meshInfo == null || textInfo.meshInfo.Length == 0)
        {
            cachedBodyMeshInfo = null;
            return;
        }

        for (int meshIndex = 0; meshIndex < textInfo.meshInfo.Length; meshIndex++)
        {
            TMP_MeshInfo meshInfo = textInfo.meshInfo[meshIndex];
            if (meshInfo.vertices == null || meshInfo.mesh == null)
            {
                cachedBodyMeshInfo = null;
                return;
            }
        }

        cachedBodyMeshInfo = textInfo.CopyMeshInfoVertexData();
    }

    private void RestoreBodyTextVertices(TMP_TextInfo textInfo)
    {
        if (cachedBodyMeshInfo == null)
        {
            return;
        }

        int meshCount = Mathf.Min(textInfo.meshInfo.Length, cachedBodyMeshInfo.Length);
        for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
        {
            Vector3[] sourceVertices = cachedBodyMeshInfo[meshIndex].vertices;
            Vector3[] targetVertices = textInfo.meshInfo[meshIndex].vertices;

            if (sourceVertices == null || targetVertices == null)
            {
                continue;
            }

            Array.Copy(sourceVertices, targetVertices, Mathf.Min(sourceVertices.Length, targetVertices.Length));
        }
    }

    private void PushBodyTextVertices(TMP_TextInfo textInfo)
    {
        for (int meshIndex = 0; meshIndex < textInfo.meshInfo.Length; meshIndex++)
        {
            textInfo.meshInfo[meshIndex].mesh.vertices = textInfo.meshInfo[meshIndex].vertices;
            bodyText.UpdateGeometry(textInfo.meshInfo[meshIndex].mesh, meshIndex);
        }
    }

    private void ApplyGlyphAnimation(TMP_TextInfo textInfo, int characterIndex, float progress)
    {
        if (characterIndex < 0 || characterIndex >= textInfo.characterCount)
        {
            return;
        }

        TMP_CharacterInfo characterInfo = textInfo.characterInfo[characterIndex];
        if (!characterInfo.isVisible)
        {
            return;
        }

        int materialIndex = characterInfo.materialReferenceIndex;
        int vertexIndex = characterInfo.vertexIndex;
        Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

        Vector3 bottomLeft = vertices[vertexIndex];
        Vector3 topRight = vertices[vertexIndex + 2];
        Vector3 midpoint = (bottomLeft + topRight) * 0.5f;

        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        float verticalOffset = Mathf.Lerp(-letterSpawnRiseDistance, 0f, eased)
            + Mathf.Sin(progress * Mathf.PI) * letterSpawnOvershootHeight;
        float scale = Mathf.Lerp(0.94f, 1f, eased) + Mathf.Sin(progress * Mathf.PI) * letterSpawnScaleBoost;
        Vector3 offset = new Vector3(0f, verticalOffset, 0f);

        for (int vertexOffset = 0; vertexOffset < 4; vertexOffset++)
        {
            int currentVertexIndex = vertexIndex + vertexOffset;
            Vector3 vertex = vertices[currentVertexIndex] - midpoint;
            vertex *= scale;
            vertex += midpoint + offset;
            vertices[currentVertexIndex] = vertex;
        }
    }

    private static IEnumerator WaitForSecondsRealtime(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private readonly struct RevealChunk
    {
        public RevealChunk(int characterCount, RevealChunkKind kind)
        {
            CharacterCount = characterCount;
            Kind = kind;
        }

        public int CharacterCount { get; }

        public RevealChunkKind Kind { get; }
    }

    private readonly struct ActiveGlyphAnimation
    {
        public ActiveGlyphAnimation(int characterIndex, float startTime)
        {
            CharacterIndex = characterIndex;
            StartTime = startTime;
        }

        public int CharacterIndex { get; }

        public float StartTime { get; }
    }

    private enum RevealChunkKind
    {
        Text,
        Punctuation,
        Whitespace
    }
}

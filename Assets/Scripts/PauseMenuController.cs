using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PauseMenuController : MonoBehaviour
{
    private const string ClassroomSceneName = "Classroom and Movement";
    private const string EvaluationSceneName = "Teacher Evaluation";
    private const string MainMenuSceneName = "Main Menu";
    private const string MainMenuScenePath = "Assets/Scenes/Main Menu.unity";

    private GameObject pauseButtonObject;
    private Button pauseButton;
    private GameObject pausePanelObject;
    private Slider musicSlider;
    private TMP_Text musicValue;
    private Toggle textToSpeechToggle;
    private TMP_Text textToSpeechValue;
    private float previousTimeScale = 1f;
    private bool isPaused;

    private static PauseMenuController instance;

    public static bool IsPointerOverPauseButton(Vector2 pointerPosition)
    {
        if (instance == null
            || instance.pauseButtonObject == null
            || !instance.pauseButtonObject.activeInHierarchy)
        {
            return false;
        }

        RectTransform pauseRect = instance.pauseButtonObject.transform as RectTransform;
        return pauseRect != null
            && RectTransformUtility.RectangleContainsScreenPoint(pauseRect, pointerPosition);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneBootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        if (scene.name != ClassroomSceneName && scene.name != EvaluationSceneName)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        PauseMenuController existing = FindFirstObjectByType<PauseMenuController>();
#else
        PauseMenuController existing = FindObjectOfType<PauseMenuController>();
#endif
        if (existing == null)
        {
            new GameObject("Pause Menu Controller").AddComponent<PauseMenuController>();
        }
    }

    private void Awake()
    {
        instance = this;
        CreateInterface();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetPaused(!isPaused);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        if (musicSlider != null)
        {
            musicSlider.onValueChanged.RemoveListener(HandleMusicVolumeChanged);
        }

        if (textToSpeechToggle != null)
        {
            textToSpeechToggle.onValueChanged.RemoveListener(HandleTextToSpeechChanged);
        }

        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveListener(TogglePause);
        }

        if (isPaused)
        {
            Time.timeScale = previousTimeScale;
        }
    }

    private void CreateInterface()
    {
        GameObject canvasObject = new GameObject(
            "Pause Menu Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 220;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform pauseButtonParent = ResolvePauseButtonParent(canvasObject.transform);
        TMP_FontAsset font = ResolveInterfaceFont();
        DialogueUiIconLibrary iconLibrary = DialogueUiIconLibrary.Load();
        pauseButton = FindExistingPauseButton(pauseButtonParent);
        if (pauseButton == null)
        {
            pauseButton = CreateStyledButton(
                pauseButtonParent,
                "Pause Button",
                "Pause",
                font,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-24f, -24f),
                new Vector2(90f, 64f));
        }

        ConfigurePauseButton(pauseButton, iconLibrary);

        pauseButtonObject = pauseButton.gameObject;
        pauseButton.onClick.RemoveListener(TogglePause);
        pauseButton.onClick.AddListener(TogglePause);

        pausePanelObject = CreatePausePanel(canvasObject.transform, font);
        pausePanelObject.SetActive(false);
    }

    private static Button FindExistingPauseButton(Transform parent)
    {
        if (parent == null)
        {
            return null;
        }

        Button[] buttons = parent.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            if (buttons[index] != null && buttons[index].name == "Pause Button")
            {
                return buttons[index];
            }
        }

        return null;
    }

    private static Transform ResolvePauseButtonParent(Transform fallback)
    {
        SceneDialogueView dialogueView = SceneDialogueView.ActiveInstance;
#if UNITY_2023_1_OR_NEWER
        dialogueView ??= FindFirstObjectByType<SceneDialogueView>();
#else
        dialogueView ??= FindObjectOfType<SceneDialogueView>();
#endif
        RectTransform dialogueCanvasRoot = dialogueView != null
            ? dialogueView.DialogueCanvasRoot
            : null;
        return dialogueCanvasRoot != null ? dialogueCanvasRoot : fallback;
    }

    private static void ConfigurePauseButton(Button button, DialogueUiIconLibrary iconLibrary)
    {
        if (button == null)
        {
            return;
        }

        RectTransform buttonRect = button.transform as RectTransform;
        if (buttonRect != null)
        {
            buttonRect.anchorMin = Vector2.one;
            buttonRect.anchorMax = Vector2.one;
            buttonRect.pivot = Vector2.one;
            buttonRect.anchoredPosition = new Vector2(-30f, -30f);
            buttonRect.sizeDelta = new Vector2(110f, 110f);
        }

        Image background = button.GetComponent<Image>();
        if (background != null && iconLibrary != null && iconLibrary.PauseButtonBackground != null)
        {
            background.sprite = iconLibrary.PauseButtonBackground;
            background.type = Image.Type.Sliced;
            background.preserveAspect = false;
            background.color = Color.white;
        }

        Transform iconTransform = button.transform.Find("Pause Icon");
        GameObject iconObject;
        if (iconTransform != null)
        {
            iconObject = iconTransform.gameObject;
        }
        else
        {
            iconObject = new GameObject(
                "Pause Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            iconObject.layer = button.gameObject.layer;
            iconObject.transform.SetParent(button.transform, false);
        }

        RectTransform iconRect = iconObject.transform as RectTransform;
        if (iconRect != null)
        {
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(60f, 60f);
        }

        Image iconImage = iconObject.GetComponent<Image>();
        if (iconImage != null)
        {
            iconImage.sprite = iconLibrary != null ? iconLibrary.PauseIcon : null;
            iconImage.type = Image.Type.Simple;
            iconImage.preserveAspect = true;
            iconImage.color = Color.white;
            iconImage.raycastTarget = false;
        }

        Shadow shadow = button.GetComponent<Shadow>();
        if (shadow != null)
        {
            shadow.enabled = false;
        }

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.gameObject.SetActive(false);
        }
    }

    private GameObject CreatePausePanel(Transform canvasTransform, TMP_FontAsset font)
    {
        GameObject overlay = CreateUiObject(
            canvasTransform,
            "Pause Overlay",
            Vector2.zero,
            Vector2.zero);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image overlayImage = overlay.AddComponent<Image>();
        overlayImage.color = new Color(0.08f, 0.12f, 0.14f, 0.58f);

        GameObject panel = CreateUiObject(
            overlay.transform,
            "Pause Panel",
            new Vector2(540f, 500f),
            Vector2.zero);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(1f, 0.955f, 0.85f, 1f);
        Outline panelOutline = panel.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.2f, 0.14f, 0.36f, 1f);
        panelOutline.effectDistance = new Vector2(6f, -6f);

        CreateText(
            panel.transform,
            "Pause Title",
            "Paused",
            font,
            42f,
            new Vector2(420f, 70f),
            new Vector2(0f, 182f));
        CreateText(
            panel.transform,
            "Music Label",
            "Music Volume",
            font,
            27f,
            new Vector2(360f, 50f),
            new Vector2(0f, 105f));

        musicSlider = CreateMusicSlider(panel.transform, new Vector2(0f, 58f));
        musicSlider.SetValueWithoutNotify(AudioSettingsStore.MusicVolume);
        musicSlider.onValueChanged.AddListener(HandleMusicVolumeChanged);

        musicValue = CreateText(
            panel.transform,
            "Music Value",
            string.Empty,
            font,
            23f,
            new Vector2(160f, 42f),
            new Vector2(0f, 17f));
        RefreshMusicValue();

        CreateText(
            panel.transform,
            "Spoken Dialogue Label",
            "Spoken Dialogue",
            font,
            25f,
            new Vector2(290f, 48f),
            new Vector2(-72f, -48f));
        textToSpeechToggle = CreateTextToSpeechToggle(
            panel.transform,
            font,
            new Vector2(155f, -48f),
            out textToSpeechValue);
        textToSpeechToggle.SetIsOnWithoutNotify(AudioSettingsStore.TextToSpeechEnabled);
        textToSpeechToggle.onValueChanged.AddListener(HandleTextToSpeechChanged);
        RefreshTextToSpeechValue();

        Button resumeButton = CreateStyledButton(
            panel.transform,
            "Resume Button",
            "Resume",
            font,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, -126f),
            new Vector2(300f, 68f));
        resumeButton.onClick.AddListener(TogglePause);

        Button mainMenuButton = CreateStyledButton(
            panel.transform,
            "Main Menu Button",
            "Main Menu",
            font,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, -202f),
            new Vector2(250f, 54f));
        mainMenuButton.onClick.AddListener(ReturnToMainMenu);
        return overlay;
    }

    private static Slider CreateMusicSlider(Transform parent, Vector2 position)
    {
        GameObject sliderObject = CreateUiObject(
            parent,
            "Music Slider",
            new Vector2(390f, 34f),
            position);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        GameObject background = CreateUiObject(
            sliderObject.transform,
            "Background",
            new Vector2(390f, 16f),
            Vector2.zero);
        Image backgroundImage = background.AddComponent<Image>();
        backgroundImage.color = new Color(0.2f, 0.14f, 0.36f, 1f);

        GameObject fillArea = CreateUiObject(
            sliderObject.transform,
            "Fill Area",
            new Vector2(362f, 12f),
            Vector2.zero);
        GameObject fill = CreateUiObject(
            fillArea.transform,
            "Fill",
            Vector2.zero,
            Vector2.zero);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.25f, 0.72f, 0.8f, 1f);

        GameObject handleArea = CreateUiObject(
            sliderObject.transform,
            "Handle Slide Area",
            new Vector2(362f, 34f),
            Vector2.zero);
        GameObject handle = CreateUiObject(
            handleArea.transform,
            "Handle",
            new Vector2(28f, 28f),
            Vector2.zero);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(1f, 0.955f, 0.85f, 1f);
        Outline handleOutline = handle.AddComponent<Outline>();
        handleOutline.effectColor = new Color(0.2f, 0.14f, 0.36f, 1f);
        handleOutline.effectDistance = new Vector2(3f, -3f);

        slider.fillRect = fillRect;
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static Toggle CreateTextToSpeechToggle(
        Transform parent,
        TMP_FontAsset font,
        Vector2 position,
        out TMP_Text valueText)
    {
        GameObject toggleObject = CreateUiObject(
            parent,
            "Spoken Dialogue Toggle",
            new Vector2(150f, 46f),
            position);
        Image background = toggleObject.AddComponent<Image>();
        background.color = new Color(0.2f, 0.14f, 0.36f, 0.18f);

        Toggle toggle = toggleObject.AddComponent<Toggle>();
        toggle.targetGraphic = background;

        GameObject check = CreateUiObject(
            toggleObject.transform,
            "Checkmark",
            new Vector2(28f, 28f),
            new Vector2(-52f, 0f));
        Image checkImage = check.AddComponent<Image>();
        checkImage.color = new Color(0.25f, 0.72f, 0.8f, 1f);
        toggle.graphic = checkImage;

        valueText = CreateText(
            toggleObject.transform,
            "Value",
            string.Empty,
            font,
            23f,
            new Vector2(82f, 40f),
            new Vector2(25f, 0f));
        return toggle;
    }

    private static Button CreateStyledButton(
        Transform parent,
        string name,
        string text,
        TMP_FontAsset fallbackFont,
        Vector2 anchor,
        Vector2 pivot,
        Vector2 position,
        Vector2 size)
    {
#if UNITY_2023_1_OR_NEWER
        DialogueExitConversationButton source = FindFirstObjectByType<DialogueExitConversationButton>();
#else
        DialogueExitConversationButton source = FindObjectOfType<DialogueExitConversationButton>();
#endif
        Image sourceImage = source != null ? source.GetComponent<Image>() : null;
        Button sourceButton = source != null ? source.GetComponent<Button>() : null;
        Shadow sourceShadow = source != null ? source.GetComponent<Shadow>() : null;
        TMP_Text sourceLabel = source != null ? source.GetComponentInChildren<TMP_Text>(true) : null;

        GameObject buttonObject = CreateUiObject(parent, name, size, position);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;

        Image image = buttonObject.AddComponent<Image>();
        if (sourceImage != null)
        {
            image.sprite = sourceImage.sprite;
            image.type = sourceImage.type;
            image.color = sourceImage.color;
            image.material = sourceImage.material;
        }
        else
        {
            image.color = new Color(0.25f, 0.72f, 0.8f, 1f);
        }

        Shadow shadow = buttonObject.AddComponent<Shadow>();
        shadow.effectColor = sourceShadow != null
            ? sourceShadow.effectColor
            : new Color(0.2f, 0.14f, 0.36f, 0.5f);
        shadow.effectDistance = sourceShadow != null
            ? sourceShadow.effectDistance
            : new Vector2(7f, -7f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        if (sourceButton != null)
        {
            button.transition = sourceButton.transition;
            button.colors = sourceButton.colors;
            button.spriteState = sourceButton.spriteState;
        }

        TMP_Text label = CreateText(
            buttonObject.transform,
            name + " Text",
            text,
            sourceLabel != null && sourceLabel.font != null ? sourceLabel.font : fallbackFont,
            sourceLabel != null ? sourceLabel.fontSize : 24f,
            size,
            Vector2.zero);
        label.fontStyle = sourceLabel != null ? sourceLabel.fontStyle : FontStyles.Normal;
        label.color = sourceLabel != null
            ? sourceLabel.color
            : new Color(0.22f, 0.12f, 0.09f, 1f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 14f;
        label.fontSizeMax = Mathf.Max(20f, label.fontSize);
        return button;
    }

    private static GameObject CreateUiObject(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 position)
    {
        GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        child.layer = 5;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return child;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string name,
        string value,
        TMP_FontAsset font,
        float fontSize,
        Vector2 size,
        Vector2 position)
    {
        GameObject textObject = CreateUiObject(parent, name, size, position);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.22f, 0.12f, 0.09f, 1f);
        text.raycastTarget = false;
        return text;
    }

    private static TMP_FontAsset ResolveInterfaceFont()
    {
#if UNITY_2023_1_OR_NEWER
        DialogueExitConversationButton source = FindFirstObjectByType<DialogueExitConversationButton>();
#else
        DialogueExitConversationButton source = FindObjectOfType<DialogueExitConversationButton>();
#endif
        TMP_Text sourceLabel = source != null ? source.GetComponentInChildren<TMP_Text>(true) : null;
        return sourceLabel != null && sourceLabel.font != null
            ? sourceLabel.font
            : TMP_Settings.defaultFontAsset;
    }

    private void TogglePause()
    {
        SetPaused(!isPaused);
    }

    private void SetPaused(bool paused)
    {
        if (paused == isPaused)
        {
            return;
        }

        isPaused = paused;
        if (paused)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = previousTimeScale;
        }

        pausePanelObject.SetActive(paused);
        pauseButtonObject.SetActive(!paused);
    }

    private void HandleMusicVolumeChanged(float value)
    {
        AudioSettingsStore.SetMusicVolume(value);
        RefreshMusicValue();
    }

    private void HandleTextToSpeechChanged(bool enabled)
    {
        AudioSettingsStore.SetTextToSpeechEnabled(enabled);
        RefreshTextToSpeechValue();
    }

    private void RefreshMusicValue()
    {
        if (musicValue != null)
        {
            musicValue.text = Mathf.RoundToInt(AudioSettingsStore.MusicVolume * 100f) + "%";
        }
    }

    private void RefreshTextToSpeechValue()
    {
        if (textToSpeechValue != null)
        {
            textToSpeechValue.text = AudioSettingsStore.TextToSpeechEnabled ? "On" : "Off";
        }
    }

    private void ReturnToMainMenu()
    {
        SetPaused(false);
        DoorSceneTransition.LoadScene(MainMenuSceneName, MainMenuScenePath);
    }
}

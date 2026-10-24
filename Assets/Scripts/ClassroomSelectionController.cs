using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class ClassroomSelectionController : MonoBehaviour
{
    private enum ButtonRole
    {
        Neutral,
        Primary,
        Navigation,
        Destructive
    }

    private const string ClassroomSceneName = "Classroom and Movement";
    private const string ClassroomScenePath = "Assets/Scenes/Classroom and Movement.unity";
    private const int MaximumUploadBytes = 12 * 1024 * 1024;
    private const int MaximumAdditionalNotesCharacters = 500;
    private const string PaperStylePreferenceKey = "Flee.ClassroomPaperStyle";
    private const string ClassroomHomePrefabPath = "Classroom Selection Views/Classroom Home";
    private const string SampleClassroomsPrefabPath = "Classroom Selection Views/Sample Classrooms";
    private const string CustomClassroomPrefabPath = "Classroom Selection Views/Custom Classroom";
    private const string SavedClassroomsPrefabPath = "Classroom Selection Views/Saved Classrooms";

    private readonly Color navy = new Color(0.075f, 0.12f, 0.2f, 1f);
    private readonly Color blue = new Color(0.16f, 0.48f, 0.78f, 1f);
    private readonly Color paleBlue = new Color(0.9f, 0.95f, 0.99f, 1f);
    private readonly Color green = new Color(0.2f, 0.64f, 0.47f, 1f);

    [Header("View Transition")]
    [Tooltip("How long each panel takes to slide in or out.")]
    [SerializeField, Min(0f)] private float viewTransitionDuration = 0.3f;
    [Tooltip("How far below its resting position a panel starts and exits, in UI pixels.")]
    [SerializeField, Min(0f)] private float viewTransitionDistance = 900f;
    [Tooltip("Controls the timing and feel of the slide.")]
    [SerializeField] private AnimationCurve viewTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Quick fade used for the title after the panel finishes sliding into place.")]
    [SerializeField, Min(0f)] private float headerFadeDuration = 0.12f;

    private RectTransform menuCanvas;
    private GameObject selectionView;
    private GameObject sampleView;
    private GameObject customView;
    private GameObject savedView;
    private GameObject sharedTitle;
    private GameObject sharedSubtitle;
    private GameObject sharedCloseButton;
    private GameObject sharedHeader;
    private CanvasGroup sharedHeaderGroup;
    private RectTransform classroomWindow;
    private RectTransform savedListRoot;
    private ScrollRect savedClassroomScrollRect;
    private TMP_InputField topicInput;
    private TMP_InputField materialInput;
    private TMP_InputField additionalNotesInput;
    private TMP_Text gradeLabel;
    private TMP_Text studentCountLabel;
    private TMP_Text languageButtonLabel;
    private TMP_Text savedClassroomStatusLabel;
    private TMP_Text statusLabel;
    private Image statusPanel;
    private Button firstRoomButton;
    private Button uploadButton;
    private Button retrieveButton;
    private TMP_Text uploadButtonLabel;
    private Button createButton;
    private TMP_Text createButtonLabel;
    private Image paperColorSwatch;
    private Sprite nineSliceButtonSprite;
    private PaperUiTheme paperTheme;
    private Image customPaperImage;
    private readonly List<Image> customPaperNotes = new List<Image>();
    private readonly List<Image> customRuledLines = new List<Image>();
    private string uploadedMaterialId;
    private string editingClassroomId;
    private bool isUploading;
    private int gradeLevel = ClassroomSelectionStore.DefaultGradeLevel;
    private int studentCountOverride;
    private int automaticStudentCount = ClassroomSelectionStore.DefaultAutomaticStudentCount;
    private int selectedPaperStyle = 1;
    private FleeClassroomLanguage classroomLanguage = FleeClassroomLanguage.English;
    private Coroutine viewTransitionRoutine;
    private GameObject activeView;
#if UNITY_EDITOR
    private bool buildViewsFromCode;
#endif

    private int ResolvedStudentCount => studentCountOverride > 0
        ? ClassroomSelectionStore.ClampStudentCount(studentCountOverride)
        : ClassroomSelectionStore.ClampStudentCount(automaticStudentCount);

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void FleePickWorksheetTextFile(string receiverName);
#endif

    public void Configure(RectTransform canvas)
    {
        menuCanvas = canvas;
        paperTheme = PaperUiTheme.Load();
        if (paperTheme != null && paperTheme.StyleCount > 0)
        {
            selectedPaperStyle = Mathf.Clamp(
                PlayerPrefs.GetInt(PaperStylePreferenceKey, selectedPaperStyle),
                0,
                paperTheme.StyleCount - 1);
        }

        ResolveNineSliceButtonSprite();
        RectTransform root = transform as RectTransform;
        Stretch(root);
        transform.SetAsLastSibling();
        BuildInterface();
        InstallPlayButtonOverlay();
        gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    public void ConfigureForPrefabBuild(RectTransform canvas)
    {
        buildViewsFromCode = true;
        Configure(canvas);
    }
#endif

    private void OnDestroy()
    {
        Button overlay = menuCanvas != null
            ? FindDescendant(menuCanvas, "Classroom Selection Button Overlay")?.GetComponent<Button>()
            : null;
        if (overlay != null)
        {
            Destroy(overlay.gameObject);
        }
    }

    public void Open()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        ShowSelection();
        if (EventSystem.current != null)
        {
            // Do not make the first classroom card look hovered before the
            // player has moved the pointer over it.
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void Close()
    {
        if (viewTransitionRoutine != null)
        {
            StopCoroutine(viewTransitionRoutine);
            viewTransitionRoutine = null;
        }

        if (!Application.isPlaying || viewTransitionDuration <= 0f || classroomWindow == null)
        {
            activeView = null;
            gameObject.SetActive(false);
            return;
        }

        SetOnlyActive(activeView);
        ResetViewState(activeView, true);
        viewTransitionRoutine = StartCoroutine(AnimateWindowClosed());
    }

    public void OpenCustom()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        BeginNewCustomClassroom();
    }

    public void OpenSaved()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        ShowSavedClassrooms();
    }

    public void OpenSamples()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        ShowSampleClassrooms();
    }

    private void BuildInterface()
    {
        if (transform.childCount > 0)
        {
            return;
        }

        Image dimmer = CreateImage("Dimmer", transform, new Color(0.025f, 0.045f, 0.08f, 0.82f));
        Stretch(dimmer.rectTransform);

        Image windowImage = CreateImage("Classroom Window", transform, Color.clear);
        windowImage.raycastTarget = false;
        RectTransform window = windowImage.rectTransform;
        classroomWindow = window;
        Anchor(window, new Vector2(0.5f, 0.5f), new Vector2(1180f, 720f), Vector2.zero);

        sharedHeader = CreatePanel("Shared Header", window);
        RectTransform sharedHeaderRect = sharedHeader.transform as RectTransform;
        Stretch(sharedHeaderRect);
        sharedHeaderGroup = sharedHeader.AddComponent<CanvasGroup>();

        sharedTitle = CreateText(
            "Title",
            sharedHeaderRect,
            "CHOOSE YOUR CLASSROOM",
            42f,
            FontStyles.Bold,
            Color.white,
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f),
            new Vector2(900f, 66f),
            new Vector2(0f, -30f)).gameObject;

        sharedSubtitle = CreateText(
            "Subtitle",
            sharedHeaderRect,
            "Pick a prepared room, or build one from your own lesson.",
            22f,
            FontStyles.Normal,
            new Color(0.88f, 0.95f, 1f, 0.9f),
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f),
            new Vector2(900f, 40f),
            new Vector2(0f, -72f)).gameObject;

        sharedCloseButton = CreateButton(
            "Close",
            window,
            "X",
            new Vector2(1f, 1f),
            new Vector2(54f, 54f),
            new Vector2(-34f, 32f),
            navy,
            Close,
            ButtonRole.Navigation).gameObject;

        selectionView = CreateOrInstantiateView(
            ClassroomHomePrefabPath,
            "Classroom Home",
            window,
            new Vector2(42f, 40f),
            new Vector2(-42f, -185f),
            view =>
            {
                ConfigureRobotPanel(view);
                BuildClassroomHome(view);
            });

        sampleView = CreateOrInstantiateView(
            SampleClassroomsPrefabPath,
            "Sample Classrooms",
            window,
            new Vector2(42f, 40f),
            new Vector2(-42f, -185f),
            view =>
            {
                ConfigureRobotPanel(view);
                BuildPreparedClassrooms(view);
            });
        sampleView.SetActive(false);

        customView = CreateOrInstantiateView(
            CustomClassroomPrefabPath,
            "Custom Classroom",
            window,
            new Vector2(62f, 40f),
            new Vector2(-62f, -42f),
            view =>
            {
                ConfigureCustomPaper(view);
                BuildCustomClassroom(view);
            });
        customView.SetActive(false);

        savedView = CreateOrInstantiateView(
            SavedClassroomsPrefabPath,
            "Saved Classrooms",
            window,
            new Vector2(62f, 40f),
            new Vector2(-62f, -185f),
            view =>
            {
                ConfigureRobotPanel(view);
                BuildSavedClassrooms(view);
            });
        savedView.SetActive(false);

        WireViewPrefabs();
        ApplySelectedPaperStyle();
        sharedHeader.transform.SetAsLastSibling();
        sharedCloseButton.transform.SetAsLastSibling();
    }

    private GameObject CreateOrInstantiateView(
        string resourcePath,
        string viewName,
        RectTransform parent,
        Vector2 offsetMin,
        Vector2 offsetMax,
        Action<RectTransform> buildFallback)
    {
        bool loadPrefab = true;
#if UNITY_EDITOR
        loadPrefab = !buildViewsFromCode;
#endif
        GameObject prefab = loadPrefab ? Resources.Load<GameObject>(resourcePath) : null;
        if (prefab != null)
        {
            GameObject instance = Instantiate(prefab, parent, false);
            instance.name = viewName;
            return instance;
        }

        GameObject view = CreatePanel(viewName, parent);
        RectTransform rect = view.transform as RectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        buildFallback?.Invoke(rect);
        return view;
    }

    private void WireViewPrefabs()
    {
        WireClassroomHomeView();
        WireSampleClassroomsView();
        WireCustomClassroomView();
        WireSavedClassroomsView();
    }

    private void WireClassroomHomeView()
    {
        BindButton(selectionView, "Create A Classroom", BeginNewCustomClassroom);
        BindButton(selectionView, "Visit A Classroom", ShowSavedClassrooms);
        BindButton(selectionView, "Sample Classrooms", ShowSampleClassrooms);
        Transform visitButton = FindDescendant(selectionView.transform, "Visit A Classroom");
        savedClassroomStatusLabel = visitButton != null
            ? visitButton.Find("Status")?.GetComponent<TMP_Text>()
            : null;
    }

    private void WireSampleClassroomsView()
    {
        firstRoomButton = BindButton(
            sampleView,
            "PHOTOSYNTHESIS",
            () => StartPreset("photosynthesis", "photosynthesis"));
        BindButton(
            sampleView,
            "FAIR INVESTIGATION",
            () => StartPreset("scientific investigation", "scientific-investigation"));
        BindButton(
            sampleView,
            "GMDAS",
            () => StartPreset("order of operations", "gmdas"));
        BindButton(
            sampleView,
            "PHILIPPINE ARCHIPELAGO",
            () => StartPreset("Philippine archipelago", "philippine-archipelago"));
        BindButton(sampleView, "Back", ShowSelection);
    }

    private void WireCustomClassroomView()
    {
        topicInput = FindComponent<TMP_InputField>(customView, "Topic Input");
        materialInput = FindComponent<TMP_InputField>(customView, "Material Input");
        additionalNotesInput = FindComponent<TMP_InputField>(customView, "Additional Notes Input");
        gradeLabel = FindComponent<TMP_Text>(customView, "Grade Value");
        studentCountLabel = FindButtonLabel(customView, "Student Count Auto");
        languageButtonLabel = FindButtonLabel(customView, "Language Toggle");
        uploadButton = FindComponent<Button>(customView, "Upload");
        uploadButtonLabel = uploadButton != null
            ? uploadButton.GetComponentInChildren<TMP_Text>(true)
            : null;
        createButton = FindComponent<Button>(customView, "Create");
        createButtonLabel = createButton != null
            ? createButton.GetComponentInChildren<TMP_Text>(true)
            : null;
        statusPanel = FindComponent<Image>(customView, "Form Status");
        statusLabel = statusPanel != null
            ? statusPanel.transform.Find("Status")?.GetComponent<TMP_Text>()
            : null;
        paperColorSwatch = FindComponent<Image>(customView, "Paper Swatch");
        customPaperImage = customView.GetComponent<Image>();

        customPaperNotes.Clear();
        AddInputNote(topicInput);
        AddInputNote(materialInput);
        AddInputNote(additionalNotesInput);
        customRuledLines.Clear();
        Image[] customImages = customView.GetComponentsInChildren<Image>(true);
        for (int index = 0; index < customImages.Length; index++)
        {
            if (customImages[index] != null
                && customImages[index].name.StartsWith("Ruled Line", StringComparison.Ordinal))
            {
                customRuledLines.Add(customImages[index]);
            }
        }

        if (topicInput != null)
        {
            topicInput.onValueChanged.RemoveAllListeners();
            topicInput.onValueChanged.AddListener(HandleTopicChanged);
        }

        if (materialInput != null)
        {
            materialInput.onValueChanged.RemoveAllListeners();
            materialInput.onValueChanged.AddListener(HandleMaterialChanged);
        }

        BindButton(customView, "Back", ShowSelection);
        BindButton(customView, "Grade Down", () => ChangeGrade(-1));
        BindButton(customView, "Grade Up", () => ChangeGrade(1));
        BindButton(customView, "Student Count Down", () => ChangeStudentCount(-1));
        BindButton(customView, "Student Count Auto", UseAutomaticStudentCount);
        BindButton(customView, "Student Count Up", () => ChangeStudentCount(1));
        BindButton(customView, "Language Toggle", ToggleLanguage);
        BindButton(customView, "Paper Colour", CyclePaperColor);
        uploadButton = BindButton(customView, "Upload", PickWorksheetFile);
        createButton = BindButton(customView, "Create", StartCustom);
        retrieveButton = BindButton(customView, "Retrieve Source", RetrieveSourceFile);
        if (retrieveButton != null)
        {
            ConfigureDownloadLabel(retrieveButton);
        }
        RefreshCustomFormState();
    }

    private void WireSavedClassroomsView()
    {
        BindButton(savedView, "Back", ShowSelection);
        if (FindDescendant(savedView.transform, "Heading Divider") == null)
        {
            CreateClassroomDivider("Heading Divider", savedView.transform, new Vector2(-4f, -134f));
        }
        savedListRoot = FindComponent<RectTransform>(savedView, "Saved Classroom List");
        savedClassroomScrollRect = FindComponent<ScrollRect>(savedView, "Saved Classroom Scroll View");
        if (savedClassroomScrollRect == null || savedListRoot == null)
        {
            return;
        }

        savedClassroomScrollRect.content = savedListRoot;
        RectTransform viewport = FindComponent<RectTransform>(savedView, "Viewport");
        savedClassroomScrollRect.viewport = viewport;
        UiScrollSettings.Apply(savedClassroomScrollRect);
    }

    private void AddInputNote(TMP_InputField input)
    {
        Image note = input != null ? input.targetGraphic as Image : null;
        if (note != null)
        {
            customPaperNotes.Add(note);
        }
    }

    private Button BindButton(
        GameObject view,
        string buttonName,
        UnityEngine.Events.UnityAction onClick)
    {
        Button button = FindComponent<Button>(view, buttonName);
        if (button == null)
        {
            return null;
        }

        button.onClick = new Button.ButtonClickedEvent();
        if (paperTheme != null)
        {
            paperTheme.ApplyButtonSlicing(button.targetGraphic as Image);
        }
        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }

        return button;
    }

    private static TMP_Text FindButtonLabel(GameObject view, string buttonName)
    {
        Button button = FindComponent<Button>(view, buttonName);
        return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private static T FindComponent<T>(GameObject view, string objectName) where T : Component
    {
        if (view == null)
        {
            return null;
        }

        Transform target = FindDescendant(view.transform, objectName);
        return target != null ? target.GetComponent<T>() : null;
    }

    private void BuildClassroomHome(RectTransform parent)
    {
        CreateClassroomHomeButton(
            "Create A Classroom",
            parent,
            "CREATE A CLASSROOM",
            "Build one from your own lesson",
            new Vector2(0f, 135f),
            BeginNewCustomClassroom);

        Button visitButton = CreateClassroomHomeButton(
            "Visit A Classroom",
            parent,
            "VISIT MY CLASSROOMS",
            "No classrooms yet",
            new Vector2(0f, 15f),
            ShowSavedClassrooms);
        savedClassroomStatusLabel = visitButton.transform.Find("Status")?.GetComponent<TMP_Text>();

        CreateClassroomHomeButton(
            "Sample Classrooms",
            parent,
            "TRY SAMPLE CLASSROOMS",
            "Check out four ready-made lessons",
            new Vector2(0f, -105f),
            ShowSampleClassrooms);
    }

    private Button CreateClassroomHomeButton(
        string name,
        RectTransform parent,
        string title,
        string description,
        Vector2 position,
        UnityEngine.Events.UnityAction onClick)
    {
        Button button = CreateButton(
            name,
            parent,
            string.Empty,
            new Vector2(0.5f, 0.5f),
            new Vector2(720f, 96f),
            position,
            Color.white,
            onClick,
            ButtonRole.Neutral);
        RectTransform buttonRect = button.transform as RectTransform;
        CreateText(
            "Title",
            buttonRect,
            title,
            24f,
            FontStyles.Bold,
            navy,
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f),
            new Vector2(660f, 34f),
            new Vector2(0f, 14f));
        CreateText(
            "Status",
            buttonRect,
            description,
            16f,
            FontStyles.Normal,
            navy,
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f),
            new Vector2(660f, 28f),
            new Vector2(0f, -19f));
        return button;
    }

    private void BuildPreparedClassrooms(RectTransform parent)
    {
        firstRoomButton = CreateRoomCard(parent, "PHOTOSYNTHESIS", "Science", "photosynthesis", "photosynthesis", new Vector2(-252f, 110f));
        CreateRoomCard(parent, "FAIR INVESTIGATION", "Science", "scientific investigation", "scientific-investigation", new Vector2(252f, 110f));
        CreateRoomCard(parent, "GMDAS", "Mathematics", "order of operations", "gmdas", new Vector2(-252f, -60f));
        CreateRoomCard(parent, "PHILIPPINE ARCHIPELAGO", "Araling Panlipunan", "Philippine archipelago", "philippine-archipelago", new Vector2(252f, -60f));

        CreateButton(
            "Back",
            parent,
            "<  BACK TO CLASSROOM MENU",
            new Vector2(0.5f, 0f),
            new Vector2(472f, 48f),
            new Vector2(0f, 52f),
            Color.white,
            ShowSelection,
            ButtonRole.Neutral);
    }

    private Button CreateRoomCard(
        RectTransform parent,
        string title,
        string subject,
        string topic,
        string presetId,
        Vector2 position)
    {
        Button button = CreateButton(
            title,
            parent,
            string.Empty,
            new Vector2(0.5f, 0.5f),
            new Vector2(472f, 140f),
            position,
            Color.white,
            () => StartPreset(topic, presetId));

        RectTransform rect = button.transform as RectTransform;
        button.GetComponent<ButtonJuice>().UsePaperCardMotion();
        Image classroomPaper = button.targetGraphic as Image;
        if (paperTheme != null)
        {
            int style = GetPresetPaperStyle(presetId);
            classroomPaper.sprite = paperTheme.GetPaper(style);
            classroomPaper.type = Image.Type.Sliced;
            classroomPaper.pixelsPerUnitMultiplier = 1f;
            classroomPaper.preserveAspect = false;
        }
        RectTransform paperRect = classroomPaper.rectTransform;

        Color cardInk = paperTheme != null ? navy : Color.white;
        string metadata = "GRADE 5  •  " + subject.ToUpperInvariant() + "  •  5–10 MIN";
        TMP_Text metadataText = CreateText("Metadata", rect, metadata, 15f, FontStyles.Bold, cardInk, TextAlignmentOptions.TopLeft, new Vector2(0.5f, 0.5f), new Vector2(410f, 30f), new Vector2(0f, 38f));
        metadataText.textWrappingMode = TextWrappingModes.NoWrap;
        metadataText.overflowMode = TextOverflowModes.Ellipsis;
        TMP_Text roomName = CreateText("Room Name", paperRect, title, 25f, FontStyles.Bold, cardInk, TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), new Vector2(410f, 58f), new Vector2(0f, 1f));
        roomName.textWrappingMode = TextWrappingModes.NoWrap;
        roomName.overflowMode = TextOverflowModes.Ellipsis;
        CreateText("Play Hint", paperRect, "Click to play", 16f, FontStyles.Normal, cardInk, TextAlignmentOptions.BottomRight, new Vector2(0.5f, 0.5f), new Vector2(410f, 30f), new Vector2(0f, -38f));
        return button;
    }

    private void BuildCustomClassroom(RectTransform parent)
    {
        CreateText("Custom Heading", parent, "CREATE YOUR OWN CLASSROOM!", 26f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(690f, 48f), new Vector2(55f, -40f));
        CreateButton(
            "Back",
            parent,
            "<  BACK TO MENU",
            new Vector2(1f, 1f),
            new Vector2(240f, 48f),
            new Vector2(-55f, -40f),
            navy,
            ShowSelection,
            ButtonRole.Navigation);

        CreateText("Topic Label", parent, "TOPIC OR LESSON NAME", 16f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(946f, 24f), new Vector2(55f, -96f));
        topicInput = CreateInputField("Topic Input", parent, "e.g. The water cycle...", false, new Vector2(0f, 1f), new Vector2(946f, 42f), new Vector2(55f, -126f));
        RegisterPaperNote(topicInput, 0);
        topicInput.characterLimit = 100;
        topicInput.onValueChanged.AddListener(HandleTopicChanged);

        CreateText("Grade Label", parent, "GRADE", 15f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(160f, 26f), new Vector2(55f, -174f));
        CreateButton("Grade Down", parent, "-", new Vector2(0f, 1f), new Vector2(40f, 48f), new Vector2(55f, -204f), blue, () => ChangeGrade(-1));
        gradeLabel = CreateText("Grade Value", parent, "5", 25f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(64f, 48f), new Vector2(103f, -204f));
        CreateButton("Grade Up", parent, "+", new Vector2(0f, 1f), new Vector2(40f, 48f), new Vector2(175f, -204f), blue, () => ChangeGrade(1));

        CreateText("Student Count Label", parent, "STUDENTS", 15f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(224f, 26f), new Vector2(263f, -174f));
        CreateButton("Student Count Down", parent, "-", new Vector2(0f, 1f), new Vector2(40f, 48f), new Vector2(263f, -204f), blue, () => ChangeStudentCount(-1));
        Button studentCountButton = CreateButton("Student Count Auto", parent, "AUTO (5)", new Vector2(0f, 1f), new Vector2(112f, 48f), new Vector2(319f, -204f), paleBlue, UseAutomaticStudentCount);
        studentCountLabel = studentCountButton.GetComponentInChildren<TMP_Text>(true);
        if (studentCountLabel != null)
        {
            studentCountLabel.color = navy;
            studentCountLabel.fontSize = 15f;
            studentCountLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }
        CreateButton("Student Count Up", parent, "+", new Vector2(0f, 1f), new Vector2(40f, 48f), new Vector2(447f, -204f), blue, () => ChangeStudentCount(1));

        CreateText("Language Label", parent, "LANGUAGE", 15f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(190f, 26f), new Vector2(535f, -174f));
        Button languageButton = CreateButton("Language Toggle", parent, "ENGLISH", new Vector2(0f, 1f), new Vector2(190f, 48f), new Vector2(535f, -204f), blue, ToggleLanguage);
        languageButtonLabel = languageButton.GetComponentInChildren<TMP_Text>(true);

        CreateText("Paper Colour Label", parent, "PAPER", 15f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(170f, 26f), new Vector2(777f, -174f));
        Button paperColorButton = CreateButton(
            "Paper Colour",
            parent,
            "<                 >",
            new Vector2(0f, 1f),
            new Vector2(170f, 48f),
            new Vector2(777f, -204f),
            blue,
            CyclePaperColor);
        paperColorSwatch = CreateImage("Paper Swatch", paperColorButton.transform, Color.white);
        paperColorSwatch.raycastTarget = false;
        Anchor(paperColorSwatch.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(86f, 30f), Vector2.zero);
        AddPaperShadow(paperColorSwatch, new Vector2(2f, -2f), 0.14f);

        CreateText("Material Label", parent, "LEARNING MATERIAL (QUESTIONS STAY GROUNDED IN THIS)", 16f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(946f, 28f), new Vector2(55f, -266f));
        materialInput = CreateInputField("Material Input", parent, "Paste worksheet text or lesson notes here...", true, new Vector2(0f, 1f), new Vector2(946f, 88f), new Vector2(55f, -298f));
        RegisterPaperNote(materialInput, 3);
        materialInput.characterLimit = ClassroomSelectionStore.MaximumMaterialCharacters;
        materialInput.onValueChanged.AddListener(HandleMaterialChanged);

        CreateText("Notes Label", parent, "SETUP NOTE (OPTIONAL)", 16f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(946f, 28f), new Vector2(55f, -396f));
        additionalNotesInput = CreateInputField("Additional Notes Input", parent, "e.g. Avoid fractions, or use our class term 'groups of'...", true, new Vector2(0f, 1f), new Vector2(946f, 48f), new Vector2(55f, -428f));
        RegisterPaperNote(additionalNotesInput, 1);
        additionalNotesInput.characterLimit = MaximumAdditionalNotesCharacters;

        statusPanel = CreateImage("Form Status", parent, Color.clear);
        statusPanel.raycastTarget = false;
        Anchor(statusPanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(946f, 36f), new Vector2(0f, 116f));
        statusLabel = CreateText("Status", statusPanel.rectTransform, string.Empty, 14f, FontStyles.Normal, navy, TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), new Vector2(926f, 36f), Vector2.zero);
        statusLabel.enableAutoSizing = true;
        statusLabel.fontSizeMin = 11f;
        statusLabel.fontSizeMax = 14f;
        statusLabel.overflowMode = TextOverflowModes.Ellipsis;
        statusPanel.gameObject.SetActive(false);

        uploadButton = CreateButton("Upload", parent, "UPLOAD WORKSHEET", new Vector2(0f, 0f), new Vector2(280f, 48f), new Vector2(55f, 52f), blue, PickWorksheetFile);
        uploadButtonLabel = uploadButton.GetComponentInChildren<TMP_Text>(true);
        retrieveButton = CreateButton("Retrieve Source", parent, "DOWNLOAD ORIGINAL FILE", new Vector2(0f, 0f), new Vector2(280f, 48f), new Vector2(351f, 52f), blue, RetrieveSourceFile);
        ConfigureDownloadLabel(retrieveButton);
        createButton = CreateButton("Create", parent, "CREATE CLASSROOM  >", new Vector2(1f, 0f), new Vector2(320f, 48f), new Vector2(-55f, 52f), green, StartCustom, ButtonRole.Primary);
        createButtonLabel = createButton.GetComponentInChildren<TMP_Text>(true);
        RefreshCustomFormState();
    }

    private void BuildSavedClassrooms(RectTransform parent)
    {
        CreateText("Saved Heading", parent, "MY CLASSROOMS", 30f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(480f, 48f), new Vector2(60f, -40f));
        CreateButton("Back", parent, "<  BACK TO MENU", new Vector2(1f, 1f), new Vector2(240f, 48f), new Vector2(-60f, -40f), navy, ShowSelection, ButtonRole.Navigation);
        CreateText(
            "Saved Description",
            parent,
            "Your custom classrooms are saved on this device.",
            18f,
            FontStyles.Normal,
            navy,
            TextAlignmentOptions.Left,
            new Vector2(0f, 1f),
            new Vector2(900f, 34f),
            new Vector2(60f, -92f));

        CreateClassroomDivider("Heading Divider", parent, new Vector2(-4f, -134f));

        GameObject scrollObject = new GameObject(
            "Saved Classroom Scroll View",
            typeof(RectTransform),
            typeof(ScrollRect));
        scrollObject.layer = 5;
        RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
        scrollRect.SetParent(parent, false);
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(44f, 56f);
        scrollRect.offsetMax = new Vector2(-44f, -134f);

        Image viewportImage = CreateImage("Viewport", scrollRect, Color.clear);
        RectTransform viewport = viewportImage.rectTransform;
        Stretch(viewport);
        viewport.offsetMax = new Vector2(-8f, 0f);
        viewportImage.raycastTarget = true;
        viewportImage.gameObject.AddComponent<RectMask2D>();

        savedListRoot = CreatePanel("Saved Classroom List", viewport).transform as RectTransform;
        savedListRoot.anchorMin = new Vector2(0f, 1f);
        savedListRoot.anchorMax = new Vector2(1f, 1f);
        savedListRoot.pivot = new Vector2(0.5f, 1f);
        savedListRoot.anchoredPosition = Vector2.zero;
        savedListRoot.sizeDelta = Vector2.zero;

        savedClassroomScrollRect = scrollObject.GetComponent<ScrollRect>();
        savedClassroomScrollRect.content = savedListRoot;
        savedClassroomScrollRect.viewport = viewport;
        savedClassroomScrollRect.horizontal = false;
        savedClassroomScrollRect.vertical = true;
        savedClassroomScrollRect.movementType = ScrollRect.MovementType.Elastic;
        savedClassroomScrollRect.elasticity = 0.12f;
        savedClassroomScrollRect.inertia = false;
        savedClassroomScrollRect.decelerationRate = 0.12f;
        UiScrollSettings.Apply(savedClassroomScrollRect);
    }

    private void InstallPlayButtonOverlay()
    {
        Transform playButton = FindDescendant(menuCanvas, "Play Button");
        if (playButton == null || playButton.Find("Classroom Selection Button Overlay") != null)
        {
            return;
        }

        GameObject overlayObject = new GameObject(
            "Classroom Selection Button Overlay",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        overlayObject.layer = 5;
        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.SetParent(playButton, false);
        Stretch(overlayRect);
        overlayRect.SetAsLastSibling();

        Image overlayImage = overlayObject.GetComponent<Image>();
        overlayImage.color = new Color(1f, 1f, 1f, 0.001f);
        Button overlayButton = overlayObject.GetComponent<Button>();
        overlayButton.targetGraphic = overlayImage;
        overlayButton.onClick.AddListener(Open);

        // The scene's original button has a persistent transition callback that
        // cannot be removed safely at runtime. Disable only that Button component;
        // its artwork stays visible while this transparent replacement handles
        // both pointer and keyboard/controller submit events.
        Button originalButton = playButton.GetComponent<Button>();
        if (originalButton != null)
        {
            originalButton.enabled = false;
        }

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(overlayObject);
        }
    }

    private void StartPreset(string topic, string presetId)
    {
        selectedPaperStyle = GetPresetPaperStyle(presetId);
        ClassroomSelectionStore.SelectPreset(topic, presetId);
        BeginClassroomTransition();
    }

    private void StartCustom()
    {
        if (createButton == null || !createButton.IsInteractable())
        {
            return;
        }
        if (isUploading)
        {
            SetStatus("AraBOT is still reading that material.", true);
            return;
        }

        string topic = topicInput != null ? topicInput.text.Trim() : string.Empty;
        string pastedMaterial = materialInput != null ? materialInput.text.Trim() : string.Empty;
        string additionalNotes = additionalNotesInput != null
            ? additionalNotesInput.text.Trim()
            : string.Empty;
        int studentCount = ResolvedStudentCount;
        if (!string.IsNullOrWhiteSpace(uploadedMaterialId))
        {
            SavedClassroomStore.Save(
                editingClassroomId,
                topic,
                string.Empty,
                uploadedMaterialId,
                gradeLevel,
                classroomLanguage,
                additionalNotes,
                studentCountOverride,
                automaticStudentCount,
                selectedPaperStyle);
            ClassroomSelectionStore.SelectMaterials(
                new[] { uploadedMaterialId },
                classroomLanguage,
                additionalNotes,
                studentCount,
                gradeLevel);
            BeginClassroomTransition();
            return;
        }

        if (!string.IsNullOrWhiteSpace(pastedMaterial))
        {
            SavedClassroomStore.Save(
                editingClassroomId,
                topic,
                pastedMaterial,
                string.Empty,
                gradeLevel,
                classroomLanguage,
                additionalNotes,
                studentCountOverride,
                automaticStudentCount,
                selectedPaperStyle);
            ClassroomSelectionStore.SelectCustom(
                topic,
                pastedMaterial,
                gradeLevel,
                classroomLanguage,
                additionalNotes,
                studentCount);
            BeginClassroomTransition();
            return;
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            SetStatus("Add a topic, paste lesson notes, or upload a worksheet first.", true);
            return;
        }

        SavedClassroomStore.Save(
            editingClassroomId,
            topic,
            string.Empty,
            string.Empty,
            gradeLevel,
            classroomLanguage,
            additionalNotes,
            studentCountOverride,
            automaticStudentCount,
            selectedPaperStyle);
        ClassroomSelectionStore.SelectCustom(
            topic,
            string.Empty,
            gradeLevel,
            classroomLanguage,
            additionalNotes,
            studentCount);
        BeginClassroomTransition();
    }

    private void BeginClassroomTransition()
    {
        FleeApiClient.GetOrCreate().ConfigurePaperStyle(selectedPaperStyle);
        gameObject.SetActive(false);
        DoorSceneTransition.LoadScene(ClassroomSceneName, ClassroomScenePath);
    }

    private void ShowSelection()
    {
        RefreshClassroomHome();
        ShowOnly(selectionView);
        SetStatus(string.Empty, false);
    }

    private void ShowSampleClassrooms()
    {
        ShowOnly(sampleView);
    }

    private void ShowCustom()
    {
        ShowOnly(customView);
        topicInput?.Select();
        topicInput?.ActivateInputField();
    }

    private void BeginNewCustomClassroom()
    {
        editingClassroomId = string.Empty;
        uploadedMaterialId = string.Empty;
        gradeLevel = ClassroomSelectionStore.DefaultGradeLevel;
        studentCountOverride = 0;
        automaticStudentCount = ClassroomSelectionStore.DefaultAutomaticStudentCount;
        classroomLanguage = FleeClassroomLanguage.English;
        if (topicInput != null)
        {
            topicInput.SetTextWithoutNotify(string.Empty);
        }

        if (materialInput != null)
        {
            materialInput.SetTextWithoutNotify(string.Empty);
        }

        if (additionalNotesInput != null)
        {
            additionalNotesInput.SetTextWithoutNotify(string.Empty);
        }

        UpdateCustomFormLabels(false);
        SetStatus(string.Empty, false);
        ShowCustom();
    }

    private void ShowSavedClassrooms()
    {
        ShowOnly(savedView);
        RefreshSavedClassrooms();
    }

    private void ShowOnly(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (viewTransitionRoutine != null)
        {
            StopCoroutine(viewTransitionRoutine);
            viewTransitionRoutine = null;
        }

        bool animateHeader = Application.isPlaying && viewTransitionDuration > 0f;
        RectTransform targetRect = target.transform as RectTransform;
        RectTransform closeRect = sharedCloseButton.transform as RectTransform;
        Vector2 restingTopRight = targetRect.offsetMax - targetRect.anchoredPosition;
        // The bottom of X sits 20 pixels above this view's actual top border.
        Anchor(closeRect, Vector2.one, new Vector2(54f, 54f),
            new Vector2(restingTopRight.x, restingTopRight.y + 74f));
        PrepareSharedHeader(target, animateHeader);

        if (!Application.isPlaying || viewTransitionDuration <= 0f)
        {
            SetOnlyActive(target);
            ResetViewState(target, true);
            activeView = target;
            return;
        }

        GameObject previous = activeView != target ? activeView : null;
        SetOnlyActive(previous);
        ResetViewState(previous, previous != null);
        target.SetActive(true);
        activeView = target;
        viewTransitionRoutine = StartCoroutine(AnimateViewChange(previous, target));
    }

    private System.Collections.IEnumerator AnimateViewChange(GameObject previous, GameObject target)
    {
        RectTransform previousRect = previous != null ? previous.transform as RectTransform : null;
        CanvasGroup previousGroup = previous != null ? GetOrAddCanvasGroup(previous) : null;
        RectTransform targetRect = target.transform as RectTransform;
        CanvasGroup targetGroup = GetOrAddCanvasGroup(target);
        float duration = Mathf.Max(0.01f, viewTransitionDuration);
        Vector2 restingPosition = Vector2.zero;
        Vector2 loweredPosition = Vector2.down * viewTransitionDistance;

        if (previousGroup != null)
        {
            previousGroup.alpha = 1f;
            previousGroup.interactable = false;
            previousGroup.blocksRaycasts = false;
        }

        targetRect.anchoredPosition = loweredPosition;
        targetGroup.alpha = 1f;
        targetGroup.interactable = false;
        targetGroup.blocksRaycasts = false;

        yield return MenuPopupMotion.Slide(
            targetRect, loweredPosition, restingPosition, duration, viewTransitionCurve, previousRect);

        if (previous != null)
        {
            previous.SetActive(false);
            ResetViewState(previous, false);
        }

        targetRect.anchoredPosition = restingPosition;
        targetGroup.alpha = 1f;
        targetGroup.interactable = true;
        targetGroup.blocksRaycasts = true;
        SetOnlyActive(target);
        if (UsesSharedHeader(target))
        {
            yield return AnimateSharedHeaderIn();
        }

        viewTransitionRoutine = null;
    }

    private System.Collections.IEnumerator AnimateWindowClosed()
    {
        CanvasGroup group = GetOrAddCanvasGroup(classroomWindow.gameObject);
        float duration = Mathf.Max(0.01f, viewTransitionDuration);
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;

        yield return MenuPopupMotion.Slide(
            classroomWindow, Vector2.zero, Vector2.down * viewTransitionDistance, duration, viewTransitionCurve);

        classroomWindow.anchoredPosition = Vector2.zero;
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
        activeView = null;
        viewTransitionRoutine = null;
        gameObject.SetActive(false);
    }

    private void SetOnlyActive(GameObject target)
    {
        selectionView?.SetActive(selectionView == target);
        sampleView?.SetActive(sampleView == target);
        customView?.SetActive(customView == target);
        savedView?.SetActive(savedView == target);
    }

    private static void ResetViewState(GameObject view, bool active)
    {
        if (view == null)
        {
            return;
        }

        RectTransform rect = view.transform as RectTransform;
        if (rect != null)
        {
            rect.anchoredPosition = Vector2.zero;
        }

        CanvasGroup group = GetOrAddCanvasGroup(view);
        group.alpha = 1f;
        group.interactable = active;
        group.blocksRaycasts = active;
        view.SetActive(active);
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        return group != null ? group : target.AddComponent<CanvasGroup>();
    }

    private void PrepareSharedHeader(GameObject target, bool animate)
    {
        if (sharedHeader == null || sharedHeaderGroup == null)
        {
            return;
        }

        bool visible = UsesSharedHeader(target);
        TMP_Text title = sharedTitle.GetComponent<TMP_Text>();
        TMP_Text subtitle = sharedSubtitle.GetComponent<TMP_Text>();
        if (target == sampleView)
        {
            title.text = "SAMPLE CLASSROOMS";
            subtitle.text = "Choose a ready-made lesson and jump straight in.";
        }
        else
        {
            title.text = "WHAT WOULD YOU LIKE TO DO?";
            subtitle.text = "Create a lesson, revisit one you saved, or try a sample.";
        }

        sharedHeader.SetActive(visible);
        sharedHeaderGroup.alpha = visible && !animate ? 1f : 0f;
        sharedHeaderGroup.interactable = visible && !animate;
        sharedHeaderGroup.blocksRaycasts = visible && !animate;
    }

    private bool UsesSharedHeader(GameObject target)
    {
        return target == selectionView || target == sampleView;
    }

    private System.Collections.IEnumerator AnimateSharedHeaderIn()
    {
        if (sharedHeader == null || sharedHeaderGroup == null)
        {
            yield break;
        }

        sharedHeader.SetActive(true);
        if (headerFadeDuration <= 0f)
        {
            sharedHeaderGroup.alpha = 1f;
        }
        else
        {
            float elapsed = 0f;
            while (elapsed < headerFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                sharedHeaderGroup.alpha = Mathf.Clamp01(elapsed / headerFadeDuration);
                yield return null;
            }

            sharedHeaderGroup.alpha = 1f;
        }

        sharedHeaderGroup.interactable = true;
        sharedHeaderGroup.blocksRaycasts = true;
    }

    private void RefreshClassroomHome()
    {
        if (savedClassroomStatusLabel == null)
        {
            return;
        }

        int savedClassroomCount = SavedClassroomStore.LoadAll().Count;
        savedClassroomStatusLabel.text = savedClassroomCount == 0
            ? "No classrooms yet — create one first"
            : savedClassroomCount == 1
                ? "1 saved classroom"
                : savedClassroomCount + " saved classrooms";
    }

    private void RefreshSavedClassrooms()
    {
        if (savedListRoot == null)
        {
            return;
        }

        for (int index = savedListRoot.childCount - 1; index >= 0; index--)
        {
            Destroy(savedListRoot.GetChild(index).gameObject);
        }

        IReadOnlyList<SavedClassroomDefinition> classrooms = SavedClassroomStore.LoadAll();
        savedListRoot.anchoredPosition = Vector2.zero;
        savedListRoot.sizeDelta = new Vector2(
            0f,
            Mathf.Max(260f, 16f + (classrooms.Count * 116f)));
        if (savedClassroomScrollRect != null)
        {
            savedClassroomScrollRect.verticalNormalizedPosition = 1f;
        }

        if (classrooms.Count == 0)
        {
            CreateText(
                "No Saved Classrooms",
                savedListRoot,
                "No custom classrooms saved yet. Create one and it will appear here.",
                21f,
                FontStyles.Italic,
                navy,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(820f, 80f),
                Vector2.zero);
            return;
        }

        for (int index = 0; index < classrooms.Count; index++)
        {
            CreateSavedClassroomRow(classrooms[index], index);
        }
    }

    private void CreateSavedClassroomRow(SavedClassroomDefinition classroom, int index)
    {
        if (classroom == null)
        {
            return;
        }

        if (index > 0)
        {
            CreateClassroomDivider("Classroom Divider", savedListRoot, new Vector2(0f, -2f - index * 116f));
        }

        GameObject row = CreatePanel("Saved " + classroom.id, savedListRoot);
        RectTransform rowRect = row.transform as RectTransform;
        Anchor(rowRect, new Vector2(0.5f, 1f), new Vector2(936f, 104f), new Vector2(0f, -8f - (index * 116f)));
        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(18, 18, 18, 18);
        rowLayout.spacing = 16f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        Image paperLabel = CreateImage(
            "Classroom Paper",
            rowRect,
            Color.white);
        if (paperTheme != null && paperTheme.StyleCount > 0)
        {
            paperLabel.sprite = paperTheme.GetPaper(classroom.paperStyle >= 0
                ? classroom.paperStyle
                : index % paperTheme.StyleCount);
            paperLabel.type = Image.Type.Sliced;
            paperLabel.preserveAspect = false;
            AddPaperShadow(paperLabel, new Vector2(3f, -3f), 0.12f);
        }
        RectTransform paperRect = paperLabel.rectTransform;
        Anchor(paperRect, new Vector2(0f, 0.5f), new Vector2(650f, 64f), new Vector2(14f, 0f));
        LayoutElement paperLayout = paperLabel.gameObject.AddComponent<LayoutElement>();
        paperLayout.flexibleWidth = 1f;
        paperLayout.preferredHeight = 68f;

        string classroomId = classroom.id;
        string title = string.IsNullOrWhiteSpace(classroom.topic) ? "My uploaded lesson" : classroom.topic;
        TMP_Text topicLabel = CreateText("Topic", paperRect, title, 20f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(500f, 30f), new Vector2(18f, 11f));
        topicLabel.rectTransform.sizeDelta = new Vector2(450f, 30f);
        topicLabel.textWrappingMode = TextWrappingModes.NoWrap;
        topicLabel.overflowMode = TextOverflowModes.Ellipsis;
        string languageName = classroom.language == FleeClassroomLanguage.Filipino
            ? "Filipino"
            : "English";
        int savedAutomaticCount = classroom.automaticStudentCount > 0
            ? classroom.automaticStudentCount
            : ClassroomSelectionStore.EstimateStudentCountFromMaterial(classroom.material);
        int savedStudentCount = classroom.studentCountOverride > 0
            ? classroom.studentCountOverride
            : savedAutomaticCount;
        CreateText("Grade", paperRect, "Grade " + classroom.gradeLevel + "  |  " + languageName + "  |  " + savedStudentCount + " students", 14f, FontStyles.Normal, navy, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(500f, 24f), new Vector2(18f, -18f));
        CreateText("Play Hint", paperRect, "CLICK TO PLAY", 12f, FontStyles.Bold, navy, TextAlignmentOptions.Right, new Vector2(1f, 0.5f), new Vector2(108f, 28f), new Vector2(-16f, 0f));

        Button paperButton = paperLabel.gameObject.AddComponent<Button>();
        paperButton.targetGraphic = paperLabel;
        paperButton.transition = Selectable.Transition.None;
        paperButton.onClick.AddListener(() => StartSavedClassroom(classroomId));
        paperLabel.gameObject.AddComponent<ButtonJuice>().UsePaperCardMotion();

        Button editButton = CreateButton("Edit", rowRect, "EDIT", new Vector2(1f, 0.5f), new Vector2(100f, 50f), new Vector2(-124f, 0f), blue, () => EditSavedClassroom(classroomId), ButtonRole.Navigation);
        LayoutElement editLayout = editButton.gameObject.AddComponent<LayoutElement>();
        editLayout.preferredWidth = 100f;
        editLayout.preferredHeight = 50f;
        TMP_Text editLabel = editButton.GetComponentInChildren<TMP_Text>(true);
        if (editLabel != null)
        {
            editLabel.fontSize = 18f;
            editLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }

        Button deleteButton = CreateButton("Delete", rowRect, "DELETE", new Vector2(1f, 0.5f), new Vector2(100f, 50f), new Vector2(-14f, 0f), new Color(0.75f, 0.25f, 0.25f), null, ButtonRole.Destructive);
        LayoutElement deleteLayout = deleteButton.gameObject.AddComponent<LayoutElement>();
        deleteLayout.preferredWidth = 100f;
        deleteLayout.preferredHeight = 50f;
        TMP_Text deleteLabel = deleteButton.GetComponentInChildren<TMP_Text>(true);
        if (deleteLabel != null)
        {
            deleteLabel.fontSize = 16f;
            deleteLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }

        bool deleteConfirmed = false;
        deleteButton.onClick.AddListener(() =>
        {
            if (!deleteConfirmed)
            {
                deleteConfirmed = true;
                if (deleteLabel != null)
                {
                    deleteLabel.text = "CONFIRM";
                }

                return;
            }

            SavedClassroomStore.Delete(classroomId);
            RefreshSavedClassrooms();
        });
    }

    private void StartSavedClassroom(string classroomId)
    {
        SavedClassroomDefinition classroom = SavedClassroomStore.Find(classroomId);
        if (classroom == null)
        {
            RefreshSavedClassrooms();
            return;
        }

        selectedPaperStyle = classroom.paperStyle >= 0 ? classroom.paperStyle : selectedPaperStyle;
        if (!string.IsNullOrWhiteSpace(classroom.materialId))
        {
            int automaticCount = classroom.automaticStudentCount > 0
                ? classroom.automaticStudentCount
                : ClassroomSelectionStore.DefaultAutomaticStudentCount;
            ClassroomSelectionStore.SelectMaterials(
                new[] { classroom.materialId },
                classroom.language,
                classroom.additionalNotes,
                classroom.studentCountOverride > 0
                    ? classroom.studentCountOverride
                    : automaticCount,
                classroom.gradeLevel);
        }
        else
        {
            int automaticCount = classroom.automaticStudentCount > 0
                ? classroom.automaticStudentCount
                : ClassroomSelectionStore.EstimateStudentCountFromMaterial(classroom.material);
            ClassroomSelectionStore.SelectCustom(
                classroom.topic,
                classroom.material,
                classroom.gradeLevel,
                classroom.language,
                classroom.additionalNotes,
                classroom.studentCountOverride > 0
                    ? classroom.studentCountOverride
                    : automaticCount);
        }

        BeginClassroomTransition();
    }

    private void EditSavedClassroom(string classroomId)
    {
        SavedClassroomDefinition classroom = SavedClassroomStore.Find(classroomId);
        if (classroom == null)
        {
            RefreshSavedClassrooms();
            return;
        }

        editingClassroomId = classroom.id;
        selectedPaperStyle = classroom.paperStyle >= 0 ? classroom.paperStyle : selectedPaperStyle;
        ApplySelectedPaperStyle();
        uploadedMaterialId = classroom.materialId ?? string.Empty;
        gradeLevel = Mathf.Clamp(classroom.gradeLevel, 1, 12);
        studentCountOverride = classroom.studentCountOverride > 0
            ? ClassroomSelectionStore.ClampStudentCount(classroom.studentCountOverride)
            : 0;
        automaticStudentCount = classroom.automaticStudentCount > 0
            ? ClassroomSelectionStore.ClampStudentCount(classroom.automaticStudentCount)
            : ClassroomSelectionStore.EstimateStudentCountFromMaterial(classroom.material);
        classroomLanguage = classroom.language;
        if (topicInput != null)
        {
            topicInput.SetTextWithoutNotify(classroom.topic ?? string.Empty);
        }

        if (materialInput != null)
        {
            materialInput.SetTextWithoutNotify(classroom.material ?? string.Empty);
        }

        if (additionalNotesInput != null)
        {
            additionalNotesInput.SetTextWithoutNotify(classroom.additionalNotes ?? string.Empty);
        }

        UpdateCustomFormLabels(true);
        SetStatus(
            string.IsNullOrWhiteSpace(uploadedMaterialId)
                ? "Edit your lesson, then choose Save & Start."
                : ClassroomSourceFiles.Exists(uploadedMaterialId)
                    ? "Worksheet attached. Download Original File gives you the PDF or image you uploaded."
                    : "Worksheet attached. Its original file was not saved on this device; upload it again to keep a downloadable copy.",
            false);
        ShowCustom();
    }

    private void UpdateCustomFormLabels(bool isEditing)
    {
        if (gradeLabel != null)
        {
            gradeLabel.text = gradeLevel.ToString();
        }

        if (createButtonLabel != null)
        {
            createButtonLabel.text = isEditing ? "SAVE & START  >" : "CREATE CLASSROOM  >";
        }

        if (languageButtonLabel != null)
        {
            languageButtonLabel.text = classroomLanguage == FleeClassroomLanguage.Filipino
                ? "FILIPINO"
                : "ENGLISH";
        }

        RefreshStudentCountLabel();
        RefreshCustomFormState();
    }

    private void ToggleLanguage()
    {
        classroomLanguage = classroomLanguage == FleeClassroomLanguage.English
            ? FleeClassroomLanguage.Filipino
            : FleeClassroomLanguage.English;
        UpdateCustomFormLabels(!string.IsNullOrWhiteSpace(editingClassroomId));
    }

    private void CyclePaperColor()
    {
        if (paperTheme == null || paperTheme.StyleCount <= 0)
        {
            return;
        }

        selectedPaperStyle = (selectedPaperStyle + 1) % paperTheme.StyleCount;
        PlayerPrefs.SetInt(PaperStylePreferenceKey, selectedPaperStyle);
        PlayerPrefs.Save();
        ApplySelectedPaperStyle();
    }

    private void ConfigureCustomPaper(RectTransform parent)
    {
        customPaperImage = ConfigureRobotPanel(parent);

        customPaperNotes.Clear();
        customRuledLines.Clear();
    }

    private Image ConfigureRobotPanel(RectTransform parent)
    {
        Image panel = parent.gameObject.GetComponent<Image>();
        if (panel == null)
        {
            panel = parent.gameObject.AddComponent<Image>();
        }

        panel.raycastTarget = false;
        panel.color = Color.white;
        panel.sprite = paperTheme != null && paperTheme.RobotPanel != null
            ? paperTheme.RobotPanel
            : paperTheme != null ? paperTheme.NeutralPaper : null;
        panel.type = panel.sprite != null ? Image.Type.Sliced : Image.Type.Simple;

        Shadow shadow = parent.gameObject.GetComponent<Shadow>();
        if (shadow == null)
        {
            shadow = parent.gameObject.AddComponent<Shadow>();
        }

        shadow.effectColor = new Color(0.02f, 0.03f, 0.05f, 0.28f);
        shadow.effectDistance = new Vector2(10f, -12f);
        shadow.useGraphicAlpha = true;
        return panel;
    }

    private void RegisterPaperNote(TMP_InputField field, int ruledLineCount)
    {
        Image note = field != null ? field.targetGraphic as Image : null;
        if (note == null)
        {
            return;
        }

        customPaperNotes.Add(note);
        if (paperTheme == null || paperTheme.RuledLine == null || ruledLineCount <= 0)
        {
            return;
        }

        for (int index = 0; index < ruledLineCount; index++)
        {
            Image line = CreateImage(
                "Ruled Line " + (customRuledLines.Count + 1),
                note.rectTransform,
                Color.white);
            line.sprite = paperTheme.RuledLine;
            line.type = Image.Type.Simple;
            line.raycastTarget = false;
            RectTransform lineRect = line.rectTransform;
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.anchoredPosition = new Vector2(0f, -28f - (index * 24f));
            lineRect.sizeDelta = new Vector2(-36f, 3f);
            line.transform.SetAsFirstSibling();
            customRuledLines.Add(line);
        }
    }

    private void ApplySelectedPaperStyle()
    {
        if (paperTheme == null || paperTheme.StyleCount <= 0)
        {
            return;
        }

        selectedPaperStyle = Mathf.Clamp(selectedPaperStyle, 0, paperTheme.StyleCount - 1);
        if (customPaperImage != null)
        {
            customPaperImage.sprite = paperTheme.RobotPanel != null
                ? paperTheme.RobotPanel
                : paperTheme.GetPaper(selectedPaperStyle);
            customPaperImage.type = Image.Type.Sliced;
        }

        for (int index = 0; index < customPaperNotes.Count; index++)
        {
            if (customPaperNotes[index] != null)
            {
                customPaperNotes[index].sprite = paperTheme.GetPaper(selectedPaperStyle);
                customPaperNotes[index].type = Image.Type.Sliced;
            }
        }

        if (paperColorSwatch != null)
        {
            paperColorSwatch.sprite = paperTheme.GetPaper(selectedPaperStyle);
            paperColorSwatch.type = Image.Type.Sliced;
            paperColorSwatch.preserveAspect = false;
        }
    }

    private void HandleMaterialChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            uploadedMaterialId = string.Empty;
        }

        automaticStudentCount = ClassroomSelectionStore.EstimateStudentCountFromMaterial(value);
        RefreshStudentCountLabel();
        RefreshCustomFormState();
    }

    private void HandleTopicChanged(string value)
    {
        RefreshCustomFormState();
        if (materialInput != null && !string.IsNullOrWhiteSpace(materialInput.text))
        {
            return;
        }

        automaticStudentCount = string.IsNullOrWhiteSpace(value)
            ? ClassroomSelectionStore.DefaultAutomaticStudentCount
            : 4;
        RefreshStudentCountLabel();
    }

    private void ChangeStudentCount(int delta)
    {
        int startingCount = studentCountOverride > 0 ? studentCountOverride : automaticStudentCount;
        studentCountOverride = ClassroomSelectionStore.ClampStudentCount(startingCount + delta);
        RefreshStudentCountLabel();
    }

    private void UseAutomaticStudentCount()
    {
        studentCountOverride = 0;
        RefreshStudentCountLabel();
    }

    private void RefreshStudentCountLabel()
    {
        if (studentCountLabel == null)
        {
            return;
        }

        studentCountLabel.text = studentCountOverride > 0
            ? studentCountOverride.ToString()
            : "AUTO (" + ResolvedStudentCount + ")";
    }

    private void RefreshCustomFormState()
    {
        bool hasTopic = topicInput != null && !string.IsNullOrWhiteSpace(topicInput.text);
        bool hasPastedMaterial = materialInput != null && !string.IsNullOrWhiteSpace(materialInput.text);
        bool hasUploadedMaterial = !string.IsNullOrWhiteSpace(uploadedMaterialId);
        bool canCreate = !isUploading && (hasTopic || hasPastedMaterial || hasUploadedMaterial);
        if (retrieveButton != null)
        {
            DisabledButtonFeedback.SetInteractable(retrieveButton, !isUploading && ClassroomSourceFiles.Exists(uploadedMaterialId));
        }

        if (uploadButton != null)
        {
            DisabledButtonFeedback.SetInteractable(uploadButton, !isUploading);
        }

        if (uploadButtonLabel != null)
        {
            uploadButtonLabel.text = isUploading
                ? "READING WORKSHEET..."
                : hasUploadedMaterial ? "UPLOAD ANOTHER" : "UPLOAD WORKSHEET";
        }

        if (createButton != null)
        {
            DisabledButtonFeedback.SetInteractable(createButton, canCreate);
        }

        if (createButtonLabel != null)
        {
            bool isEditing = !string.IsNullOrWhiteSpace(editingClassroomId);
            createButtonLabel.text = isUploading
                ? "WAITING FOR MATERIAL..."
                : canCreate
                    ? (isEditing ? "SAVE & START  >" : "CREATE CLASSROOM  >")
                    : "ADD A TOPIC OR MATERIAL";
        }
    }

    private void ChangeGrade(int delta)
    {
        gradeLevel = Mathf.Clamp(gradeLevel + delta, 1, 12);
        if (gradeLabel != null)
        {
            gradeLabel.text = gradeLevel.ToString();
        }
    }

    private void PickWorksheetFile()
    {
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanelWithFilters(
            "Choose learning material",
            string.Empty,
            new[]
            {
                "Learning material", "pdf,png,jpg,jpeg,webp",
                "All files", "*"
            });
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            UploadMaterialBytes(
                Path.GetFileName(path),
                File.ReadAllBytes(path),
                GetContentType(path));
        }
        catch (Exception exception)
        {
            SetStatus("Could not read that file: " + exception.Message, true);
        }
#elif UNITY_WEBGL
        FleePickWorksheetTextFile(gameObject.name);
#else
        SetStatus("File picking is available in the Editor and WebGL build. You can paste text here.", true);
#endif
    }

    // Called by the WebGL file picker plug-in.
    public void OnWorksheetFilePicked(string payload)
    {
        int firstSeparator = payload.IndexOf('\n');
        int secondSeparator = firstSeparator >= 0 ? payload.IndexOf('\n', firstSeparator + 1) : -1;
        if (firstSeparator <= 0 || secondSeparator <= firstSeparator + 1 || secondSeparator >= payload.Length - 1)
        {
            SetStatus("The selected file could not be read.", true);
            return;
        }

        try
        {
            string fileName = Uri.UnescapeDataString(payload.Substring(0, firstSeparator));
            string contentType = Uri.UnescapeDataString(payload.Substring(
                firstSeparator + 1,
                secondSeparator - firstSeparator - 1));
            byte[] contents = Convert.FromBase64String(payload.Substring(secondSeparator + 1));
            UploadMaterialBytes(fileName, contents, contentType);
        }
        catch (Exception)
        {
            SetStatus("The selected file could not be read.", true);
        }
    }

    public void OnWorksheetFileFailed(string message)
    {
        SetStatus(string.IsNullOrWhiteSpace(message) ? "The file could not be read." : message, true);
    }

    private void UploadMaterialBytes(string fileName, byte[] contents, string contentType)
    {
        if (contents == null || contents.Length == 0)
        {
            SetStatus("That file was empty.", true);
            return;
        }

        if (contents.Length > MaximumUploadBytes)
        {
            SetStatus("That file is larger than 12 MB. Choose a smaller worksheet.", true);
            return;
        }

        StartCoroutine(UploadMaterialAndOptionallyStart(fileName, contents, contentType, false));
    }

    private System.Collections.IEnumerator UploadMaterialAndOptionallyStart(
        string fileName,
        byte[] contents,
        string contentType,
        bool startWhenReady)
    {
        isUploading = true;
        uploadedMaterialId = string.Empty;
        SetUploadButtonsInteractable(false);

        FleeMaterialSession material = null;
        FleeApiFailure failure = null;
        yield return FleeApiClient.GetOrCreate().UploadClassroomMaterial(
            fileName,
            contents,
            contentType,
            value => material = value,
            error => failure = error,
            (_, message) => SetStatus(message, false));

        isUploading = false;
        SetUploadButtonsInteractable(true);
        if (failure != null || material == null)
        {
            SetStatus(
                failure != null
                    ? "Could not read the material: " + failure.Message
                    : "The material did not return a usable lesson.",
                true);
            yield break;
        }

        uploadedMaterialId = material.MaterialId;
        try
        {
            ClassroomSourceFiles.Save(uploadedMaterialId, fileName, contentType, contents);
        }
        catch (Exception exception)
        {
            SetStatus("The worksheet was read, but its original file could not be stored on this device: " + exception.Message, true);
            RefreshCustomFormState();
            yield break;
        }
        automaticStudentCount = ClassroomSelectionStore.EstimateStudentCountFromUpload(material);
        RefreshStudentCountLabel();
        if (topicInput != null && string.IsNullOrWhiteSpace(topicInput.text))
        {
            topicInput.SetTextWithoutNotify(material.Topics.Length > 0
                ? material.Topics[0]
                : (!string.IsNullOrWhiteSpace(material.Subject)
                    ? material.Subject
                    : Path.GetFileNameWithoutExtension(fileName)));
        }

        RefreshCustomFormState();

        string topicSummary = material.Topics.Length > 0
            ? " Topics found: " + string.Join(", ", material.Topics)
            : string.Empty;
        string unreadableWarning = material.Unreadable.Length > 0
            ? " Some parts could not be read; please check the source image."
            : string.Empty;
        SetStatus(
            fileName + " is ready." + topicSummary +
            " Suggested class size: " + automaticStudentCount + " students. Choose Create Classroom to play, or Download Original File to get your upload." +
            unreadableWarning,
            false);

        if (startWhenReady)
        {
            ClassroomSelectionStore.SelectMaterials(
                new[] { uploadedMaterialId },
                classroomLanguage,
                additionalNotesInput != null ? additionalNotesInput.text : string.Empty,
                ResolvedStudentCount,
                gradeLevel);
            BeginClassroomTransition();
        }
    }

    private void SetUploadButtonsInteractable(bool interactable)
    {
        if (uploadButton != null)
        {
            DisabledButtonFeedback.SetInteractable(uploadButton, interactable && !isUploading);
        }

        RefreshCustomFormState();
    }

    private void RetrieveSourceFile()
    {
        if (retrieveButton == null || !retrieveButton.IsInteractable())
        {
            return;
        }
        try
        {
            ClassroomSourceFiles.Retrieve(uploadedMaterialId);
        }
        catch (Exception exception)
        {
            SetStatus("Could not retrieve the original file: " + exception.Message, true);
        }
    }

    private static void ConfigureDownloadLabel(Button button)
    {
        (button.transform as RectTransform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 280f);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(24f, 8f);
        label.rectTransform.offsetMax = new Vector2(-24f, -8f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 18f;
    }

    private static int GetPresetPaperStyle(string presetId)
    {
        switch (presetId)
        {
            case "photosynthesis":
                return 5;
            case "scientific-investigation":
                return 1;
            case "gmdas":
                return 7;
            default:
                return 9;
        }
    }

    private void CreateClassroomDivider(string name, Transform parent, Vector2 position)
    {
        Image divider = CreateImage(name, parent, new Color(navy.r, navy.g, navy.b, 0.18f));
        divider.raycastTarget = false;
        Anchor(divider.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 2f), position);
    }

    private static string GetContentType(string fileName)
    {
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".pdf":
                return "application/pdf";
            case ".png":
                return "image/png";
            case ".jpg":
            case ".jpeg":
                return "image/jpeg";
            case ".webp":
                return "image/webp";
            case ".csv":
                return "text/csv";
            case ".md":
            case ".markdown":
                return "text/markdown";
            default:
                return "text/plain";
        }
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusLabel == null)
        {
            return;
        }

        bool hasMessage = !string.IsNullOrWhiteSpace(message);
        if (statusPanel != null)
        {
            statusPanel.gameObject.SetActive(hasMessage);
        }

        statusLabel.text = !hasMessage
            ? string.Empty
            : (isError ? "Please check: " : isUploading ? "Worksheet upload: " : "Classroom setup: ") + message.Trim();
    }

    private static GameObject CreatePanel(string name, Transform parent)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform));
        panel.layer = 5;
        panel.transform.SetParent(parent, false);
        return panel;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.layer = 5;
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private Button CreateButton(
        string name,
        Transform parent,
        string label,
        Vector2 anchor,
        Vector2 size,
        Vector2 position,
        Color color,
        UnityEngine.Events.UnityAction onClick,
        ButtonRole role = ButtonRole.Neutral)
    {
        Image image = CreateImage(name, parent, Color.white);
        RectTransform rect = image.rectTransform;
        Anchor(rect, anchor, size, position);

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        image.gameObject.AddComponent<DisabledButtonFeedback>();
        Sprite themedButtonSprite = paperTheme != null && paperTheme.RobotButton != null
            ? paperTheme.RobotButton
            : paperTheme != null ? paperTheme.NeutralPaper : null;
        if (themedButtonSprite != null)
        {
            image.sprite = themedButtonSprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
        }
        else if (nineSliceButtonSprite != null)
        {
            image.sprite = nineSliceButtonSprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
        }
        button.transition = Selectable.Transition.None;
        if (paperTheme != null)
        {
            paperTheme.ApplyButtonSlicing(image);
        }
        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }

        if (!string.IsNullOrEmpty(label))
        {
            TMP_Text buttonLabel = CreateText(
                "Label",
                rect,
                label,
                20f,
                FontStyles.Bold,
                themedButtonSprite != null ? navy : Color.white,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                size - new Vector2(20f, 12f),
                Vector2.zero);
            buttonLabel.textWrappingMode = TextWrappingModes.NoWrap;
            buttonLabel.overflowMode = TextOverflowModes.Ellipsis;
        }

        image.gameObject.AddComponent<ButtonJuice>();
        if (themedButtonSprite != null)
        {
            AddPaperShadow(image, new Vector2(4f, -5f), 0.2f);
        }

        return button;
    }

    private Color GetButtonRoleColor(ButtonRole role, Color fallback)
    {
        switch (role)
        {
            case ButtonRole.Primary:
                return green;
            case ButtonRole.Navigation:
                return blue;
            case ButtonRole.Destructive:
                return new Color(0.78f, 0.22f, 0.24f, 1f);
            default:
                return fallback;
        }
    }

    private Color GetButtonLabelColor(ButtonRole role)
    {
        switch (role)
        {
            case ButtonRole.Primary:
                return new Color(0.04f, 0.28f, 0.18f, 1f);
            case ButtonRole.Destructive:
                return new Color(0.42f, 0.06f, 0.08f, 1f);
            default:
                return navy;
        }
    }

    private void ResolveNineSliceButtonSprite()
    {
        nineSliceButtonSprite = null;
        if (menuCanvas == null)
        {
            return;
        }

        Image[] images = menuCanvas.GetComponentsInChildren<Image>(true);
        for (int index = 0; index < images.Length; index++)
        {
            Image image = images[index];
            if (image == null || image.sprite == null || image.type != Image.Type.Sliced)
            {
                continue;
            }

            Vector4 border = image.sprite.border;
            if (border.x > 0f || border.y > 0f || border.z > 0f || border.w > 0f)
            {
                nineSliceButtonSprite = image.sprite;
                return;
            }
        }
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        float size,
        FontStyles style,
        Color color,
        TextAlignmentOptions alignment,
        Vector2 anchor,
        Vector2 dimensions,
        Vector2 position)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.layer = 5;
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        Anchor(rect, anchor, dimensions, position);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private TMP_InputField CreateInputField(
        string name,
        Transform parent,
        string placeholder,
        bool multiline,
        Vector2 anchor,
        Vector2 size,
        Vector2 position)
    {
        Image background = CreateImage(name, parent, paleBlue);
        if (paperTheme != null)
        {
            background.sprite = paperTheme.GetPaper(1);
            background.type = Image.Type.Sliced;
            background.color = new Color(1f, 1f, 1f, 0.92f);
            AddPaperShadow(background, new Vector2(3f, -4f), 0.13f);
        }
        RectTransform fieldRect = background.rectTransform;
        Anchor(fieldRect, anchor, size, position);

        TMP_InputField field = background.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.lineType = multiline
            ? TMP_InputField.LineType.MultiLineNewline
            : TMP_InputField.LineType.SingleLine;

        GameObject viewportObject = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewportObject.layer = 5;
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.SetParent(fieldRect, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(18f, 10f);
        viewport.offsetMax = new Vector2(-18f, -10f);

        TMP_Text placeholderText = CreateText("Placeholder", viewport, placeholder, 18f, FontStyles.Italic, new Color(navy.r, navy.g, navy.b, 0.45f), multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RectTransform placeholderRect = placeholderText.rectTransform;
        Stretch(placeholderRect);

        TMP_Text inputText = CreateText("Text", viewport, string.Empty, 18f, FontStyles.Normal, navy, multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RectTransform inputRect = inputText.rectTransform;
        Stretch(inputRect);

        field.textViewport = viewport;
        field.textComponent = inputText;
        field.placeholder = placeholderText;
        field.caretColor = navy;
        field.selectionColor = new Color(0.25f, 0.55f, 0.9f, 0.35f);
        return field;
    }

    private static void AddPaperShadow(Image image, Vector2 distance, float alpha)
    {
        if (image == null || image.GetComponent<Shadow>() != null)
        {
            return;
        }

        Shadow shadow = image.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.02f, 0.03f, 0.05f, alpha);
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private static Transform FindDescendant(Transform parent, string targetName)
    {
        if (parent == null)
        {
            return null;
        }

        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (string.Equals(child.name, targetName, StringComparison.Ordinal))
            {
                return child;
            }

            Transform result = FindDescendant(child, targetName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static void Stretch(RectTransform rect)
    {
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }
}

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
    private GameObject customView;
    private GameObject savedView;
    private GameObject sharedTitle;
    private GameObject sharedSubtitle;
    private GameObject sharedCloseButton;
    private GameObject sharedHeader;
    private CanvasGroup sharedHeaderGroup;
    private RectTransform classroomWindow;
    private RectTransform savedListRoot;
    private TMP_InputField topicInput;
    private TMP_InputField materialInput;
    private TMP_InputField additionalNotesInput;
    private TMP_Text gradeLabel;
    private TMP_Text studentCountLabel;
    private TMP_Text languageButtonLabel;
    private TMP_Text statusLabel;
    private Image statusPanel;
    private Button firstRoomButton;
    private Button uploadButton;
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
            sharedHeaderRect,
            "X",
            new Vector2(1f, 1f),
            new Vector2(54f, 54f),
            new Vector2(-34f, -34f),
            navy,
            Close,
            ButtonRole.Navigation).gameObject;

        selectionView = CreatePanel("Prepared Classrooms", window);
        RectTransform selectionRect = selectionView.transform as RectTransform;
        selectionRect.anchorMin = Vector2.zero;
        selectionRect.anchorMax = Vector2.one;
        selectionRect.offsetMin = new Vector2(42f, 40f);
        selectionRect.offsetMax = new Vector2(-42f, -185f);
        ConfigureRobotPanel(selectionRect);
        BuildPreparedClassrooms(selectionRect);

        customView = CreatePanel("Custom Classroom", window);
        RectTransform customRect = customView.transform as RectTransform;
        customRect.anchorMin = Vector2.zero;
        customRect.anchorMax = Vector2.one;
        customRect.offsetMin = new Vector2(62f, 40f);
        customRect.offsetMax = new Vector2(-62f, -42f);
        ConfigureCustomPaper(customRect);
        BuildCustomClassroom(customRect);
        ApplySelectedPaperStyle();
        customView.SetActive(false);

        savedView = CreatePanel("Saved Classrooms", window);
        RectTransform savedRect = savedView.transform as RectTransform;
        savedRect.anchorMin = Vector2.zero;
        savedRect.anchorMax = Vector2.one;
        savedRect.offsetMin = new Vector2(62f, 40f);
        savedRect.offsetMax = new Vector2(-62f, -185f);
        ConfigureRobotPanel(savedRect);
        BuildSavedClassrooms(savedRect);
        savedView.SetActive(false);
        sharedHeader.transform.SetAsLastSibling();
    }

    private void BuildPreparedClassrooms(RectTransform parent)
    {
        firstRoomButton = CreateRoomCard(parent, "PHOTOSYNTHESIS", "Science", "photosynthesis", "photosynthesis", new Vector2(-252f, 125f), new Color(0.3f, 0.68f, 0.42f));
        CreateRoomCard(parent, "FAIR INVESTIGATION", "Science", "scientific investigation", "scientific-investigation", new Vector2(252f, 125f), new Color(0.28f, 0.55f, 0.82f));
        CreateRoomCard(parent, "GMDAS", "Mathematics", "order of operations", "gmdas", new Vector2(-252f, -60f), new Color(0.93f, 0.59f, 0.2f));
        CreateRoomCard(parent, "PHILIPPINE ARCHIPELAGO", "Araling Panlipunan", "Philippine archipelago", "philippine-archipelago", new Vector2(252f, -60f), new Color(0.78f, 0.32f, 0.35f));

        CreateButton(
            "Create Your Own",
            parent,
            "+  CREATE YOUR OWN CLASSROOM",
            new Vector2(0.5f, 0f),
            new Vector2(472f, 64f),
            new Vector2(-252f, 18f),
            navy,
            BeginNewCustomClassroom,
            ButtonRole.Primary);
        CreateButton(
            "My Classrooms",
            parent,
            "MY CLASSROOMS",
            new Vector2(0.5f, 0f),
            new Vector2(472f, 64f),
            new Vector2(252f, 18f),
            blue,
            ShowSavedClassrooms,
            ButtonRole.Navigation);
    }

    private Button CreateRoomCard(
        RectTransform parent,
        string title,
        string subject,
        string topic,
        string presetId,
        Vector2 position,
        Color color)
    {
        Button button = CreateButton(
            title,
            parent,
            string.Empty,
            new Vector2(0.5f, 0.5f),
            new Vector2(472f, 165f),
            position,
            color,
            () => StartPreset(topic, presetId));

        RectTransform rect = button.transform as RectTransform;
        Image tile = button.targetGraphic as Image;
        if (tile != null && paperTheme != null)
        {
            tile.sprite = paperTheme.GetPaper(paperTheme.FindClosestStyle(color));
            tile.type = Image.Type.Sliced;
            tile.preserveAspect = false;
        }

        Color cardInk = paperTheme != null ? navy : Color.white;
        string metadata = "GRADE 5  •  " + subject.ToUpperInvariant() + "  •  5–10 MIN";
        TMP_Text metadataText = CreateText("Metadata", rect, metadata, 15f, FontStyles.Bold, cardInk, TextAlignmentOptions.TopLeft, new Vector2(0.5f, 0.5f), new Vector2(410f, 30f), new Vector2(0f, 45f));
        metadataText.textWrappingMode = TextWrappingModes.NoWrap;
        metadataText.overflowMode = TextOverflowModes.Ellipsis;
        TMP_Text roomName = CreateText("Room Name", rect, title, 25f, FontStyles.Bold, cardInk, TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), new Vector2(410f, 58f), new Vector2(0f, 1f));
        roomName.textWrappingMode = TextWrappingModes.NoWrap;
        roomName.overflowMode = TextOverflowModes.Ellipsis;
        CreateText("Enter classroom  >", rect, "Enter classroom  >", 16f, FontStyles.Normal, cardInk, TextAlignmentOptions.BottomRight, new Vector2(0.5f, 0.5f), new Vector2(410f, 34f), new Vector2(0f, -45f));
        return button;
    }

    private void BuildCustomClassroom(RectTransform parent)
    {
        CreateText("Custom Heading", parent, "CREATE YOUR OWN", 30f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(520f, 48f), new Vector2(55f, -22f));
        CreateButton(
            "Back",
            parent,
            "<  BACK TO ROOMS",
            new Vector2(1f, 1f),
            new Vector2(240f, 48f),
            new Vector2(-55f, -22f),
            navy,
            ShowSelection,
            ButtonRole.Navigation);

        CreateText("Topic Label", parent, "TOPIC OR LESSON NAME", 16f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(946f, 28f), new Vector2(55f, -78f));
        topicInput = CreateInputField("Topic Input", parent, "e.g. The water cycle", false, new Vector2(0f, 1f), new Vector2(946f, 52f), new Vector2(55f, -108f));
        RegisterPaperNote(topicInput, 0);
        topicInput.characterLimit = 100;
        topicInput.onValueChanged.AddListener(HandleTopicChanged);

        CreateText("Grade Label", parent, "GRADE", 15f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(160f, 26f), new Vector2(55f, -174f));
        CreateButton("Grade Down", parent, "-", new Vector2(0f, 1f), new Vector2(48f, 48f), new Vector2(55f, -204f), blue, () => ChangeGrade(-1));
        gradeLabel = CreateText("Grade Value", parent, "5", 25f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(64f, 48f), new Vector2(103f, -204f));
        CreateButton("Grade Up", parent, "+", new Vector2(0f, 1f), new Vector2(48f, 48f), new Vector2(167f, -204f), blue, () => ChangeGrade(1));

        CreateText("Student Count Label", parent, "STUDENTS", 15f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(224f, 26f), new Vector2(263f, -174f));
        CreateButton("Student Count Down", parent, "-", new Vector2(0f, 1f), new Vector2(48f, 48f), new Vector2(263f, -204f), blue, () => ChangeStudentCount(-1));
        Button studentCountButton = CreateButton("Student Count Auto", parent, "AUTO 5", new Vector2(0f, 1f), new Vector2(128f, 48f), new Vector2(311f, -204f), paleBlue, UseAutomaticStudentCount);
        studentCountLabel = studentCountButton.GetComponentInChildren<TMP_Text>(true);
        if (studentCountLabel != null)
        {
            studentCountLabel.color = navy;
            studentCountLabel.fontSize = 15f;
            studentCountLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }
        CreateButton("Student Count Up", parent, "+", new Vector2(0f, 1f), new Vector2(48f, 48f), new Vector2(439f, -204f), blue, () => ChangeStudentCount(1));

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
        additionalNotesInput = CreateInputField("Additional Notes Input", parent, "e.g. Avoid fractions, or use our class term 'groups of'", true, new Vector2(0f, 1f), new Vector2(946f, 62f), new Vector2(55f, -428f));
        RegisterPaperNote(additionalNotesInput, 1);
        additionalNotesInput.characterLimit = MaximumAdditionalNotesCharacters;

        statusPanel = CreateImage("Form Status", parent, new Color(0.86f, 0.94f, 1f, 0.98f));
        if (paperTheme != null && paperTheme.RobotSection != null)
        {
            statusPanel.sprite = paperTheme.RobotSection;
            statusPanel.type = Image.Type.Sliced;
        }
        statusPanel.raycastTarget = false;
        Anchor(statusPanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(680f, 46f), new Vector2(0f, 80f));
        statusLabel = CreateText("Status", statusPanel.rectTransform, string.Empty, 14f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(640f, 34f), Vector2.zero);
        statusLabel.enableAutoSizing = true;
        statusLabel.fontSizeMin = 11f;
        statusLabel.fontSizeMax = 14f;
        statusLabel.overflowMode = TextOverflowModes.Ellipsis;
        statusPanel.gameObject.SetActive(false);

        uploadButton = CreateButton("Upload", parent, "UPLOAD WORKSHEET", new Vector2(0f, 0f), new Vector2(280f, 54f), new Vector2(55f, 18f), blue, PickWorksheetFile);
        uploadButtonLabel = uploadButton.GetComponentInChildren<TMP_Text>(true);
        CreateText("Formats", parent, "PDF, PNG, JPG, or WEBP", 14f, FontStyles.Normal, navy, TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(300f, 50f), new Vector2(351f, 20f));
        createButton = CreateButton("Create", parent, "CREATE CLASSROOM  >", new Vector2(1f, 0f), new Vector2(320f, 56f), new Vector2(-55f, 18f), green, StartCustom, ButtonRole.Primary);
        createButtonLabel = createButton.GetComponentInChildren<TMP_Text>(true);
        RefreshCustomFormState();
    }

    private void BuildSavedClassrooms(RectTransform parent)
    {
        CreateText("Saved Heading", parent, "MY CLASSROOMS", 30f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(480f, 48f), new Vector2(60f, -25f));
        CreateButton("Back", parent, "<  BACK TO ROOMS", new Vector2(1f, 1f), new Vector2(240f, 48f), new Vector2(-60f, -25f), navy, ShowSelection, ButtonRole.Navigation);
        CreateText(
            "Saved Description",
            parent,
            "Your five most recent custom classroom recipes are saved on this device.",
            18f,
            FontStyles.Normal,
            navy,
            TextAlignmentOptions.Left,
            new Vector2(0f, 1f),
            new Vector2(900f, 34f),
            new Vector2(60f, -66f));

        savedListRoot = CreatePanel("Saved Classroom List", parent).transform as RectTransform;
        savedListRoot.anchorMin = Vector2.zero;
        savedListRoot.anchorMax = Vector2.one;
        savedListRoot.offsetMin = new Vector2(36f, 10f);
        savedListRoot.offsetMax = new Vector2(-36f, -90f);
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
        ClassroomSelectionStore.SelectPreset(topic, presetId);
        BeginClassroomTransition();
    }

    private void StartCustom()
    {
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
                automaticStudentCount);
            ClassroomSelectionStore.SelectMaterials(
                new[] { uploadedMaterialId },
                classroomLanguage,
                additionalNotes,
                studentCount);
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
                automaticStudentCount);
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
            automaticStudentCount);
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
        gameObject.SetActive(false);
        DoorSceneTransition.LoadScene(ClassroomSceneName, ClassroomScenePath);
    }

    private void ShowSelection()
    {
        ShowOnly(selectionView);
        SetStatus(string.Empty, false);
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
        float elapsed = 0f;
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

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = viewTransitionCurve != null
                ? viewTransitionCurve.Evaluate(progress)
                : progress;

            if (previousRect != null)
            {
                previousRect.anchoredPosition = Vector2.LerpUnclamped(
                    restingPosition,
                    loweredPosition,
                    eased);
            }

            targetRect.anchoredPosition = Vector2.LerpUnclamped(
                loweredPosition,
                restingPosition,
                eased);
            yield return null;
        }

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
        if (target != customView)
        {
            yield return AnimateSharedHeaderIn();
        }

        viewTransitionRoutine = null;
    }

    private System.Collections.IEnumerator AnimateWindowClosed()
    {
        CanvasGroup group = GetOrAddCanvasGroup(classroomWindow.gameObject);
        float duration = Mathf.Max(0.01f, viewTransitionDuration);
        float elapsed = 0f;
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = viewTransitionCurve != null
                ? viewTransitionCurve.Evaluate(progress)
                : progress;
            classroomWindow.anchoredPosition = Vector2.down * (viewTransitionDistance * eased);
            yield return null;
        }

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

        bool visible = target != customView;
        sharedHeader.SetActive(visible);
        sharedHeaderGroup.alpha = visible && !animate ? 1f : 0f;
        sharedHeaderGroup.interactable = visible && !animate;
        sharedHeaderGroup.blocksRaycasts = visible && !animate;
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

        Image row = CreateImage(
            "Saved " + classroom.id,
            savedListRoot,
            paleBlue);
        Sprite rowSprite = paperTheme != null && paperTheme.RobotSection != null
            ? paperTheme.RobotSection
            : paperTheme != null ? paperTheme.RobotButton : null;
        if (rowSprite != null)
        {
            row.sprite = rowSprite;
            row.type = Image.Type.Sliced;
            row.preserveAspect = false;
            row.color = Color.white;
            AddPaperShadow(row, new Vector2(4f, -5f), 0.18f);
        }
        RectTransform rowRect = row.rectTransform;
        Anchor(rowRect, new Vector2(0.5f, 1f), new Vector2(936f, 82f), new Vector2(0f, -8f - (index * 86f)));

        Image paperLabel = CreateImage(
            "Classroom Paper",
            rowRect,
            index % 2 == 0 ? paleBlue : Color.Lerp(paleBlue, Color.white, 0.35f));
        if (paperTheme != null && paperTheme.StyleCount > 0)
        {
            paperLabel.sprite = paperTheme.GetPaper(1 + (index * 4));
            paperLabel.type = Image.Type.Sliced;
            paperLabel.preserveAspect = false;
            paperLabel.color = Color.white;
            AddPaperShadow(paperLabel, new Vector2(3f, -3f), 0.12f);
        }
        RectTransform paperRect = paperLabel.rectTransform;
        Anchor(paperRect, new Vector2(0f, 0.5f), new Vector2(650f, 64f), new Vector2(14f, 0f));

        string classroomId = classroom.id;
        string title = string.IsNullOrWhiteSpace(classroom.topic) ? "My uploaded lesson" : classroom.topic;
        TMP_Text topicLabel = CreateText("Topic", paperRect, title, 20f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(500f, 30f), new Vector2(18f, 11f));
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
        string countSource = classroom.studentCountOverride > 0 ? string.Empty : " auto";
        CreateText("Grade", paperRect, "Grade " + classroom.gradeLevel + "  |  " + languageName + "  |  " + savedStudentCount + countSource + " students", 14f, FontStyles.Normal, navy, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(500f, 24f), new Vector2(18f, -18f));
        CreateText("Open Hint", paperRect, "OPEN  >", 14f, FontStyles.Bold, navy, TextAlignmentOptions.Right, new Vector2(1f, 0.5f), new Vector2(108f, 28f), new Vector2(-16f, 0f));

        Button paperButton = paperLabel.gameObject.AddComponent<Button>();
        paperButton.targetGraphic = paperLabel;
        ColorBlock paperColors = paperButton.colors;
        paperColors.normalColor = Color.white;
        paperColors.highlightedColor = new Color(0.95f, 0.98f, 1f, 1f);
        paperColors.pressedColor = new Color(0.86f, 0.9f, 0.94f, 1f);
        paperButton.colors = paperColors;
        paperButton.onClick.AddListener(() => StartSavedClassroom(classroomId));
        paperLabel.gameObject.AddComponent<ButtonJuice>();

        Button editButton = CreateButton("Edit", rowRect, "EDIT", new Vector2(1f, 0.5f), new Vector2(100f, 50f), new Vector2(-124f, 0f), blue, () => EditSavedClassroom(classroomId), ButtonRole.Navigation);
        TMP_Text editLabel = editButton.GetComponentInChildren<TMP_Text>(true);
        if (editLabel != null)
        {
            editLabel.fontSize = 18f;
            editLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }

        Button deleteButton = CreateButton("Delete", rowRect, "DELETE", new Vector2(1f, 0.5f), new Vector2(100f, 50f), new Vector2(-14f, 0f), new Color(0.75f, 0.25f, 0.25f), null, ButtonRole.Destructive);
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
                    : automaticCount);
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
                ? "Edit the classroom, then save and start."
                : "This classroom uses a previously uploaded worksheet.",
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
            customPaperImage.color = Color.white;
        }

        for (int index = 0; index < customPaperNotes.Count; index++)
        {
            if (customPaperNotes[index] != null)
            {
                customPaperNotes[index].sprite = paperTheme.GetPaper(selectedPaperStyle);
                customPaperNotes[index].type = Image.Type.Sliced;
                customPaperNotes[index].color = new Color(1f, 1f, 1f, 0.98f);
            }
        }

        Color accent = paperTheme.GetAccent(selectedPaperStyle);
        for (int index = 0; index < customRuledLines.Count; index++)
        {
            if (customRuledLines[index] != null)
            {
                customRuledLines[index].color = new Color(accent.r, accent.g, accent.b, 0.16f);
            }
        }

        if (paperColorSwatch != null)
        {
            paperColorSwatch.sprite = paperTheme.GetPaper(selectedPaperStyle);
            paperColorSwatch.type = Image.Type.Sliced;
            paperColorSwatch.preserveAspect = false;
            paperColorSwatch.color = Color.white;
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
            : "AUTO " + ResolvedStudentCount;
    }

    private void RefreshCustomFormState()
    {
        bool hasTopic = topicInput != null && !string.IsNullOrWhiteSpace(topicInput.text);
        bool hasPastedMaterial = materialInput != null && !string.IsNullOrWhiteSpace(materialInput.text);
        bool hasUploadedMaterial = !string.IsNullOrWhiteSpace(uploadedMaterialId);
        bool canCreate = !isUploading && (hasTopic || hasPastedMaterial || hasUploadedMaterial);

        if (uploadButton != null)
        {
            uploadButton.interactable = !isUploading;
        }

        if (uploadButtonLabel != null)
        {
            uploadButtonLabel.text = isUploading
                ? "READING WORKSHEET..."
                : hasUploadedMaterial ? "UPLOAD ANOTHER" : "UPLOAD WORKSHEET";
        }

        if (createButton != null)
        {
            createButton.interactable = canCreate;
        }

        if (createButtonLabel != null)
        {
            bool isEditing = !string.IsNullOrWhiteSpace(editingClassroomId);
            createButtonLabel.text = isUploading
                ? "WAITING FOR MATERIAL..."
                : canCreate
                    ? (isEditing ? "SAVE & START  >" : "CREATE CLASSROOM  >")
                    : "ADD A TOPIC OR MATERIAL";
            createButtonLabel.color = canCreate
                ? GetButtonLabelColor(ButtonRole.Primary)
                : new Color(navy.r, navy.g, navy.b, 0.52f);
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
            " Auto roster: " + automaticStudentCount + " students." +
            unreadableWarning,
            false);

        if (startWhenReady)
        {
            ClassroomSelectionStore.SelectMaterials(
                new[] { uploadedMaterialId },
                classroomLanguage,
                additionalNotesInput != null ? additionalNotesInput.text : string.Empty,
                ResolvedStudentCount);
            BeginClassroomTransition();
        }
    }

    private void SetUploadButtonsInteractable(bool interactable)
    {
        if (uploadButton != null)
        {
            uploadButton.interactable = interactable && !isUploading;
        }

        RefreshCustomFormState();
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
            statusPanel.color = isError
                ? new Color(1f, 0.83f, 0.8f, 1f)
                : isUploading
                    ? new Color(0.82f, 0.92f, 1f, 1f)
                    : new Color(0.82f, 0.95f, 0.86f, 1f);
        }

        statusLabel.text = !hasMessage
            ? string.Empty
            : (isError ? "!  " : isUploading ? "READING:  " : "READY:  ") + message.Trim();
        statusLabel.color = isError ? new Color(0.55f, 0.08f, 0.08f) : navy;
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
        Image image = CreateImage(name, parent, color);
        RectTransform rect = image.rectTransform;
        Anchor(rect, anchor, size, position);

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Sprite themedButtonSprite = paperTheme != null && paperTheme.RobotButton != null
            ? paperTheme.RobotButton
            : paperTheme != null ? paperTheme.GetPaper(paperTheme.FindClosestStyle(color)) : null;
        Color roleColor = GetButtonRoleColor(role, color);
        Color normalTint = themedButtonSprite != null && role != ButtonRole.Neutral
            ? Color.Lerp(Color.white, roleColor, 0.16f)
            : themedButtonSprite != null ? Color.white : color;
        if (themedButtonSprite != null)
        {
            image.sprite = themedButtonSprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.color = normalTint;
        }
        else if (nineSliceButtonSprite != null)
        {
            image.sprite = nineSliceButtonSprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
        }
        ColorBlock colors = button.colors;
        colors.normalColor = normalTint;
        colors.highlightedColor = Color.Lerp(normalTint, Color.white, 0.16f);
        colors.pressedColor = Color.Lerp(normalTint, Color.black, 0.14f);
        colors.disabledColor = new Color(0.62f, 0.68f, 0.72f, 0.72f);
        button.colors = colors;
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
                themedButtonSprite != null ? GetButtonLabelColor(role) : Color.white,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                size - new Vector2(20f, 12f),
                Vector2.zero);
            buttonLabel.textWrappingMode = TextWrappingModes.NoWrap;
            buttonLabel.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (themedButtonSprite != null && role != ButtonRole.Neutral)
        {
            Image accent = CreateImage("Role Accent", rect, roleColor);
            accent.raycastTarget = false;
            RectTransform accentRect = accent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(1f, 0f);
            accentRect.pivot = new Vector2(0.5f, 0f);
            accentRect.offsetMin = new Vector2(13f, 7f);
            accentRect.offsetMax = new Vector2(-13f, 13f);
            accent.transform.SetAsFirstSibling();
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

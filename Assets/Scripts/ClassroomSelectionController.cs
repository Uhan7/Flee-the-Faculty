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
    private const string ClassroomSceneName = "Classroom and Movement";
    private const string ClassroomScenePath = "Assets/Scenes/Classroom and Movement.unity";
    private const int MaximumUploadBytes = 12 * 1024 * 1024;

    private readonly Color navy = new Color(0.075f, 0.12f, 0.2f, 1f);
    private readonly Color blue = new Color(0.16f, 0.48f, 0.78f, 1f);
    private readonly Color paleBlue = new Color(0.9f, 0.95f, 0.99f, 1f);
    private readonly Color cream = new Color(1f, 0.98f, 0.92f, 1f);
    private readonly Color green = new Color(0.2f, 0.64f, 0.47f, 1f);

    private RectTransform menuCanvas;
    private GameObject selectionView;
    private GameObject customView;
    private GameObject savedView;
    private RectTransform savedListRoot;
    private TMP_InputField topicInput;
    private TMP_InputField materialInput;
    private TMP_Text gradeLabel;
    private TMP_Text statusLabel;
    private Button firstRoomButton;
    private Button uploadButton;
    private Button createButton;
    private TMP_Text createButtonLabel;
    private string uploadedMaterialId;
    private string editingClassroomId;
    private bool isUploading;
    private int gradeLevel = ClassroomSelectionStore.DefaultGradeLevel;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void FleePickWorksheetTextFile(string receiverName);
#endif

    public void Configure(RectTransform canvas)
    {
        menuCanvas = canvas;
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
        if (EventSystem.current != null && firstRoomButton != null)
        {
            EventSystem.current.SetSelectedGameObject(firstRoomButton.gameObject);
        }
    }

    public void Close()
    {
        gameObject.SetActive(false);
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

        RectTransform window = CreateImage("Classroom Window", transform, cream).rectTransform;
        Anchor(window, new Vector2(0.5f, 0.5f), new Vector2(1180f, 720f), Vector2.zero);

        CreateText(
            "Title",
            window,
            "CHOOSE YOUR CLASSROOM",
            42f,
            FontStyles.Bold,
            navy,
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f),
            new Vector2(900f, 66f),
            new Vector2(0f, -50f));

        CreateText(
            "Subtitle",
            window,
            "Pick a prepared room, or build one from your own lesson.",
            22f,
            FontStyles.Normal,
            navy,
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f),
            new Vector2(900f, 40f),
            new Vector2(0f, -102f));

        CreateButton("Close", window, "X", new Vector2(1f, 1f), new Vector2(54f, 54f), new Vector2(-34f, -34f), navy, Close);

        selectionView = CreatePanel("Prepared Classrooms", window);
        RectTransform selectionRect = selectionView.transform as RectTransform;
        selectionRect.anchorMin = Vector2.zero;
        selectionRect.anchorMax = Vector2.one;
        selectionRect.offsetMin = new Vector2(42f, 40f);
        selectionRect.offsetMax = new Vector2(-42f, -145f);
        BuildPreparedClassrooms(selectionRect);

        customView = CreatePanel("Custom Classroom", window);
        RectTransform customRect = customView.transform as RectTransform;
        customRect.anchorMin = Vector2.zero;
        customRect.anchorMax = Vector2.one;
        customRect.offsetMin = new Vector2(62f, 40f);
        customRect.offsetMax = new Vector2(-62f, -145f);
        BuildCustomClassroom(customRect);
        customView.SetActive(false);

        savedView = CreatePanel("Saved Classrooms", window);
        RectTransform savedRect = savedView.transform as RectTransform;
        savedRect.anchorMin = Vector2.zero;
        savedRect.anchorMax = Vector2.one;
        savedRect.offsetMin = new Vector2(62f, 40f);
        savedRect.offsetMax = new Vector2(-62f, -145f);
        BuildSavedClassrooms(savedRect);
        savedView.SetActive(false);
    }

    private void BuildPreparedClassrooms(RectTransform parent)
    {
        firstRoomButton = CreateRoomCard(parent, "PHOTOSYNTHESIS", "Science", "photosynthesis", "photosynthesis", new Vector2(-280f, 170f), new Color(0.3f, 0.68f, 0.42f));
        CreateRoomCard(parent, "FAIR INVESTIGATION", "Science", "scientific investigation", "scientific-investigation", new Vector2(280f, 170f), new Color(0.28f, 0.55f, 0.82f));
        CreateRoomCard(parent, "GMDAS", "Mathematics", "order of operations", "gmdas", new Vector2(-280f, -65f), new Color(0.93f, 0.59f, 0.2f));
        CreateRoomCard(parent, "PHILIPPINE ARCHIPELAGO", "Araling Panlipunan", "Philippine archipelago", "philippine-archipelago", new Vector2(280f, -65f), new Color(0.78f, 0.32f, 0.35f));

        CreateButton(
            "Create Your Own",
            parent,
            "+  CREATE YOUR OWN CLASSROOM",
            new Vector2(0.5f, 0f),
            new Vector2(500f, 68f),
            new Vector2(-270f, 18f),
            navy,
            BeginNewCustomClassroom);
        CreateButton(
            "My Classrooms",
            parent,
            "MY CLASSROOMS",
            new Vector2(0.5f, 0f),
            new Vector2(500f, 68f),
            new Vector2(270f, 18f),
            blue,
            ShowSavedClassrooms);
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
            new Vector2(520f, 195f),
            position,
            color,
            () => StartPreset(topic, presetId));

        RectTransform rect = button.transform as RectTransform;
        CreateText("Subject", rect, subject.ToUpperInvariant(), 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.TopLeft, new Vector2(0.5f, 0.5f), new Vector2(450f, 40f), new Vector2(0f, 55f));
        CreateText("Room Name", rect, title, 28f, FontStyles.Bold, Color.white, TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), new Vector2(450f, 76f), new Vector2(0f, 2f));
        CreateText("Enter classroom  >", rect, "Enter classroom  >", 17f, FontStyles.Normal, Color.white, TextAlignmentOptions.BottomRight, new Vector2(0.5f, 0.5f), new Vector2(450f, 40f), new Vector2(0f, -57f));
        return button;
    }

    private void BuildCustomClassroom(RectTransform parent)
    {
        CreateText("Custom Heading", parent, "CREATE YOUR OWN", 30f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(480f, 48f), new Vector2(0f, -25f));
        CreateButton("Back", parent, "<  BACK TO ROOMS", new Vector2(1f, 1f), new Vector2(240f, 48f), new Vector2(-120f, -25f), navy, ShowSelection);

        CreateText("Topic Label", parent, "TOPIC OR LESSON NAME", 17f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(500f, 34f), new Vector2(0f, -90f));
        topicInput = CreateInputField("Topic Input", parent, "e.g. The water cycle", false, new Vector2(0f, 1f), new Vector2(650f, 62f), new Vector2(0f, -136f));
        topicInput.characterLimit = 100;

        CreateText("Grade Label", parent, "GRADE LEVEL", 17f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(1f, 1f), new Vector2(250f, 34f), new Vector2(-125f, -90f));
        CreateButton("Grade Down", parent, "-", new Vector2(1f, 1f), new Vector2(54f, 54f), new Vector2(-230f, -137f), blue, () => ChangeGrade(-1));
        gradeLabel = CreateText("Grade Value", parent, "5", 27f, FontStyles.Bold, navy, TextAlignmentOptions.Center, new Vector2(1f, 1f), new Vector2(90f, 54f), new Vector2(-155f, -137f));
        CreateButton("Grade Up", parent, "+", new Vector2(1f, 1f), new Vector2(54f, 54f), new Vector2(-80f, -137f), blue, () => ChangeGrade(1));

        CreateText("Material Label", parent, "LEARNING MATERIAL (QUESTIONS WILL STAY GROUNDED IN THIS)", 17f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(760f, 34f), new Vector2(0f, -202f));
        materialInput = CreateInputField("Material Input", parent, "Paste worksheet text or lesson notes here...", true, new Vector2(0f, 1f), new Vector2(1040f, 160f), new Vector2(0f, -242f));
        materialInput.characterLimit = ClassroomSelectionStore.MaximumMaterialCharacters;
        materialInput.onValueChanged.AddListener(HandleMaterialChanged);

        uploadButton = CreateButton("Upload", parent, "UPLOAD WORKSHEET", new Vector2(0f, 0f), new Vector2(280f, 58f), new Vector2(0f, 82f), blue, PickWorksheetFile);
        CreateText("Formats", parent, "PDF or worksheet photo (PNG, JPG, WEBP)", 15f, FontStyles.Normal, navy, TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(420f, 54f), new Vector2(300f, 82f));

        statusLabel = CreateText("Status", parent, string.Empty, 16f, FontStyles.Bold, new Color(0.7f, 0.18f, 0.18f), TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(900f, 38f), new Vector2(0f, 34f));
        createButton = CreateButton("Create", parent, "CREATE CLASSROOM  >", new Vector2(1f, 0f), new Vector2(320f, 64f), new Vector2(0f, 82f), green, StartCustom);
        createButtonLabel = createButton.GetComponentInChildren<TMP_Text>(true);
    }

    private void BuildSavedClassrooms(RectTransform parent)
    {
        CreateText("Saved Heading", parent, "MY CLASSROOMS", 30f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(480f, 48f), new Vector2(0f, -25f));
        CreateButton("Back", parent, "<  BACK TO ROOMS", new Vector2(1f, 1f), new Vector2(240f, 48f), new Vector2(-120f, -25f), navy, ShowSelection);
        CreateText(
            "Saved Description",
            parent,
            "Your five most recent custom classroom recipes are saved on this device.",
            18f,
            FontStyles.Normal,
            navy,
            TextAlignmentOptions.Left,
            new Vector2(0f, 1f),
            new Vector2(900f, 38f),
            new Vector2(0f, -68f));

        savedListRoot = CreatePanel("Saved Classroom List", parent).transform as RectTransform;
        savedListRoot.anchorMin = Vector2.zero;
        savedListRoot.anchorMax = Vector2.one;
        savedListRoot.offsetMin = new Vector2(0f, 0f);
        savedListRoot.offsetMax = new Vector2(0f, -95f);
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
        if (!string.IsNullOrWhiteSpace(uploadedMaterialId))
        {
            SavedClassroomStore.Save(
                editingClassroomId,
                topic,
                string.Empty,
                uploadedMaterialId,
                gradeLevel);
            ClassroomSelectionStore.SelectMaterials(uploadedMaterialId);
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
                gradeLevel);
            ClassroomSelectionStore.SelectCustom(topic, pastedMaterial, gradeLevel);
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
            gradeLevel);
        ClassroomSelectionStore.SelectCustom(topic, string.Empty, gradeLevel);
        BeginClassroomTransition();
    }

    private void BeginClassroomTransition()
    {
        gameObject.SetActive(false);
        DoorSceneTransition.LoadScene(ClassroomSceneName, ClassroomScenePath);
    }

    private void ShowSelection()
    {
        selectionView?.SetActive(true);
        customView?.SetActive(false);
        savedView?.SetActive(false);
        SetStatus(string.Empty, false);
    }

    private void ShowCustom()
    {
        selectionView?.SetActive(false);
        customView?.SetActive(true);
        savedView?.SetActive(false);
        topicInput?.Select();
        topicInput?.ActivateInputField();
    }

    private void BeginNewCustomClassroom()
    {
        editingClassroomId = string.Empty;
        uploadedMaterialId = string.Empty;
        gradeLevel = ClassroomSelectionStore.DefaultGradeLevel;
        if (topicInput != null)
        {
            topicInput.text = string.Empty;
        }

        if (materialInput != null)
        {
            materialInput.text = string.Empty;
        }

        UpdateCustomFormLabels(false);
        SetStatus(string.Empty, false);
        ShowCustom();
    }

    private void ShowSavedClassrooms()
    {
        selectionView?.SetActive(false);
        customView?.SetActive(false);
        savedView?.SetActive(true);
        RefreshSavedClassrooms();
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
            index % 2 == 0 ? paleBlue : Color.Lerp(paleBlue, Color.white, 0.35f));
        RectTransform rowRect = row.rectTransform;
        Anchor(rowRect, new Vector2(0.5f, 1f), new Vector2(1040f, 78f), new Vector2(0f, -10f - (index * 88f)));

        string classroomId = classroom.id;
        string title = string.IsNullOrWhiteSpace(classroom.topic) ? "My uploaded lesson" : classroom.topic;
        CreateText("Topic", rowRect, title, 21f, FontStyles.Bold, navy, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(510f, 34f), new Vector2(20f, 11f));
        CreateText("Grade", rowRect, "Grade " + classroom.gradeLevel, 15f, FontStyles.Normal, navy, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(510f, 28f), new Vector2(20f, -20f));

        CreateButton("Enter", rowRect, "ENTER", new Vector2(1f, 0.5f), new Vector2(170f, 50f), new Vector2(-278f, 0f), green, () => StartSavedClassroom(classroomId));
        CreateButton("Edit", rowRect, "EDIT", new Vector2(1f, 0.5f), new Vector2(120f, 50f), new Vector2(-148f, 0f), blue, () => EditSavedClassroom(classroomId));
        Button deleteButton = CreateButton("Delete", rowRect, "DELETE", new Vector2(1f, 0.5f), new Vector2(120f, 50f), new Vector2(-10f, 0f), new Color(0.75f, 0.25f, 0.25f), null);
        TMP_Text deleteLabel = deleteButton.GetComponentInChildren<TMP_Text>(true);
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
            ClassroomSelectionStore.SelectMaterials(classroom.materialId);
        }
        else
        {
            ClassroomSelectionStore.SelectCustom(
                classroom.topic,
                classroom.material,
                classroom.gradeLevel);
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
        if (topicInput != null)
        {
            topicInput.text = classroom.topic ?? string.Empty;
        }

        if (materialInput != null)
        {
            materialInput.text = classroom.material ?? string.Empty;
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
    }

    private void HandleMaterialChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            uploadedMaterialId = string.Empty;
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
        if (topicInput != null && string.IsNullOrWhiteSpace(topicInput.text))
        {
            topicInput.text = material.Topics.Length > 0
                ? material.Topics[0]
                : (!string.IsNullOrWhiteSpace(material.Subject)
                    ? material.Subject
                    : Path.GetFileNameWithoutExtension(fileName));
        }

        string topicSummary = material.Topics.Length > 0
            ? " Topics found: " + string.Join(", ", material.Topics)
            : string.Empty;
        string unreadableWarning = material.Unreadable.Length > 0
            ? " Some parts could not be read; please check the source image."
            : string.Empty;
        SetStatus(fileName + " is ready." + topicSummary + unreadableWarning, false);

        if (startWhenReady)
        {
            ClassroomSelectionStore.SelectMaterials(uploadedMaterialId);
            BeginClassroomTransition();
        }
    }

    private void SetUploadButtonsInteractable(bool interactable)
    {
        if (uploadButton != null)
        {
            uploadButton.interactable = interactable;
        }

        if (createButton != null)
        {
            createButton.interactable = interactable;
        }
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

        statusLabel.text = message;
        statusLabel.color = isError ? new Color(0.72f, 0.18f, 0.18f) : green;
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

    private static Button CreateButton(
        string name,
        Transform parent,
        string label,
        Vector2 anchor,
        Vector2 size,
        Vector2 position,
        Color color,
        UnityEngine.Events.UnityAction onClick)
    {
        Image image = CreateImage(name, parent, color);
        RectTransform rect = image.rectTransform;
        Anchor(rect, anchor, size, position);

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.14f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.12f);
        button.colors = colors;
        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }

        if (!string.IsNullOrEmpty(label))
        {
            CreateText("Label", rect, label, 20f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), size - new Vector2(20f, 12f), Vector2.zero);
        }

        return button;
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

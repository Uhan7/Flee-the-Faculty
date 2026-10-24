#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class PaperUiAssetSetup
{
    private const string AtlasPath = "Assets/Sprites/UI Stuff/Extra Paper UI.png";
    private const string RobotAtlasPath = "Assets/Sprites/UI Stuff/9-Slice Boxes.png";
    private const string ThemePath = "Assets/Resources/Paper UI Theme.asset";
    private const float SourceHeight = 2670f;

    [MenuItem("Tools/Flee the Faculty/Configure Extra Paper UI")]
    public static void Configure()
    {
        TextureImporter importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError("Extra Paper UI.png could not be imported at " + AtlasPath + ".");
            return;
        }

        List<SpriteMetaData> sprites = new List<SpriteMetaData>
        {
            Sprite("Paper Neutral", 50f, 63f, 394f, 395f, 16f),

            Sprite("Paper Aqua", 18f, 566f, 394f, 394f, 16f),
            Sprite("Paper Blue", 504f, 568f, 394f, 394f, 16f),
            Sprite("Paper Purple", 988f, 587f, 394f, 394f, 16f),
            Sprite("Paper Violet", 1448f, 598f, 394f, 394f, 16f),
            Sprite("Paper Pink", 1967f, 600f, 394f, 394f, 16f),
            Sprite("Paper Green", 41f, 1121f, 394f, 394f, 16f),
            Sprite("Paper Mint", 525f, 1125f, 395f, 394f, 16f),
            Sprite("Paper Yellow", 1009f, 1144f, 395f, 394f, 16f),
            Sprite("Paper Orange", 1468f, 1153f, 394f, 394f, 16f),
            Sprite("Paper Red", 1986f, 1155f, 394f, 394f, 16f),

            Sprite("Paper Rounded Aqua", 25f, 1669f, 394f, 394f, 72f),
            Sprite("Paper Rounded Blue", 511f, 1671f, 395f, 395f, 72f),
            Sprite("Paper Rounded Purple", 995f, 1691f, 395f, 394f, 72f),
            Sprite("Paper Rounded Violet", 1456f, 1701f, 394f, 395f, 72f),
            Sprite("Paper Rounded Pink", 1974f, 1704f, 395f, 394f, 72f),
            Sprite("Paper Rounded Green", 49f, 2224f, 394f, 394f, 72f),
            Sprite("Paper Rounded Mint", 533f, 2229f, 394f, 394f, 72f),
            Sprite("Paper Rounded Yellow", 1017f, 2248f, 394f, 394f, 72f),
            Sprite("Paper Rounded Orange", 1475f, 2257f, 394f, 394f, 72f),
            Sprite("Paper Rounded Red", 1994f, 2259f, 394f, 394f, 72f),

            Sprite("Paper Ruled Line", 49f, 23f, 398f, 5f),
            Sprite("Paper Tape", 2002f, 356f, 222f, 63f, 12f),
            Sprite("Paper Clip", 2280f, 18f, 106f, 162f),
            Sprite("Paper Thumbtack", 2106f, 146f, 36f, 35f)
        };

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.spritePixelsPerUnit = 100f;
#pragma warning disable 0618
        importer.spritesheet = sprites.ToArray();
#pragma warning restore 0618
        importer.SaveAndReimport();

        Dictionary<string, Sprite> importedSprites = AssetDatabase.LoadAllAssetsAtPath(AtlasPath)
            .OfType<Sprite>()
            .ToDictionary(sprite => sprite.name, sprite => sprite);
        Dictionary<string, Sprite> robotSprites = AssetDatabase.LoadAllAssetsAtPath(RobotAtlasPath)
            .OfType<Sprite>()
            .ToDictionary(sprite => sprite.name, sprite => sprite);

        PaperUiTheme theme = AssetDatabase.LoadAssetAtPath<PaperUiTheme>(ThemePath);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<PaperUiTheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
        }

        theme.Configure(
            Get(importedSprites, "Paper Neutral"),
            GetSeries(importedSprites, "Paper "),
            GetSeries(importedSprites, "Paper Rounded "),
            Get(importedSprites, "Paper Ruled Line"),
            Get(importedSprites, "Paper Tape"),
            Get(importedSprites, "Paper Clip"),
            Get(importedSprites, "Paper Thumbtack"),
            Get(robotSprites, "9-Slice Boxes_10"),
            Get(robotSprites, "9-Slice Boxes_6"),
            Get(robotSprites, "9-Slice Boxes_0"));
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Extra Paper UI atlas and Paper UI Theme are ready.", theme);
    }

    [MenuItem("Tools/Flee the Faculty/Validate Paper UI")]
    public static void Validate()
    {
        PaperUiTheme theme = AssetDatabase.LoadAssetAtPath<PaperUiTheme>(ThemePath);
        Require(theme != null, "Paper UI Theme asset is missing.");
        Require(theme.StyleCount == 10, "Paper UI Theme should contain ten paper colours.");
        Require(theme.NeutralPaper != null, "Neutral paper sprite is missing.");
        Require(theme.RuledLine != null, "Ruled-line sprite is missing.");
        Require(theme.Tape != null && theme.PaperClip != null && theme.Thumbtack != null,
            "One or more paper decorations are missing.");
        Require(HasSliceBorder(theme.RobotPanel) &&
            HasSliceBorder(theme.RobotSection) &&
            HasSliceBorder(theme.RobotButton),
            "An AraBOT panel, section, or button sprite is missing its 9-slice border.");
        Require(theme.RobotPanel != theme.RobotSection &&
            theme.RobotSection != theme.RobotButton &&
            theme.RobotPanel != theme.RobotButton,
            "AraBOT depth levels should use three distinct 9-slice variants.");
        for (int index = 0; index < theme.StyleCount; index++)
        {
            Require(HasSliceBorder(theme.GetPaper(index)),
                "Rigid paper style " + index + " is missing its 9-slice border.");
            Require(HasSliceBorder(theme.GetRoundedPaper(index)),
                "Rounded tile style " + index + " is missing its 9-slice border.");
        }

        GameObject canvasObject = new GameObject("Paper UI Validation Canvas", typeof(RectTransform), typeof(Canvas));
        GameObject classroomObject = null;
        GameObject reportObject = null;
        try
        {
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1600f, 900f);

            GameObject classroomPrefab = Resources.Load<GameObject>("Classroom Selection");
            Require(classroomPrefab != null, "Classroom Selection prefab is missing.");
            classroomObject = UnityEngine.Object.Instantiate(classroomPrefab, canvasRect);
            ClassroomSelectionController controller = classroomObject.GetComponent<ClassroomSelectionController>();
            Require(controller != null, "Classroom Selection controller is missing.");
            controller.Configure(canvasRect);

            controller.Open();
            Canvas.ForceUpdateCanvases();
            Image classroomWindow = FindDeep(classroomObject.transform, "Classroom Window")?.GetComponent<Image>();
            Require(classroomWindow != null && classroomWindow.color.a <= 0.001f,
                "Classroom screens should sit on the dim overlay without an opaque cream backing.");
            Transform preparedPanel = FindDeep(classroomObject.transform, "Prepared Classrooms");
            Transform savedPanel = FindDeep(classroomObject.transform, "Saved Classrooms");
            Require(preparedPanel?.GetComponent<Image>()?.sprite == theme.RobotPanel &&
                savedPanel?.GetComponent<Image>()?.sprite == theme.RobotPanel,
                "Classroom selection and saved classrooms should use AraBOT outer panels.");
            Transform roomTile = FindDeep(classroomObject.transform, "PHOTOSYNTHESIS");
            RequireSlicedPaper(roomTile, false, "Prepared classroom tile");
            TMP_Text roomMetadata = FindDeep(roomTile, "Metadata")?.GetComponent<TMP_Text>();
            Require(roomMetadata != null &&
                roomMetadata.text.Contains("GRADE 5") &&
                roomMetadata.text.Contains("5–10 MIN"),
                "Prepared classroom cards should show compact grade and duration metadata.");
            Transform createOwnButton = FindDeep(classroomObject.transform, "Create Your Own");
            RequireSlicedPaper(createOwnButton, false, "Create-your-own button");
            Require(createOwnButton.GetComponent<Image>()?.sprite == theme.RobotButton,
                "Classroom controls should use the AraBOT button texture.");
            Require(createOwnButton.GetComponent<Shadow>() != null,
                "Paper buttons should have a soft shadow.");

            controller.OpenCustom();
            Canvas.ForceUpdateCanvases();

            Transform customPaper = FindDeep(classroomObject.transform, "Custom Classroom");
            Require(customPaper != null, "Custom Classroom paper was not created.");
            Image customPaperImage = customPaper.GetComponent<Image>();
            Require(customPaperImage != null && customPaperImage.sprite == theme.RobotPanel &&
                customPaperImage.type == Image.Type.Sliced,
                "Custom Classroom did not receive the AraBOT 9-sliced panel.");
            Require(CountNamedChildren(customPaper, "Ruled Line ") >= 4,
                "Custom Classroom paper notes did not receive enough ruled lines.");
            Require(FindDeep(customPaper, "Paper Colour") != null,
                "Custom Classroom paper-colour control is missing.");
            Image paperSwatch = FindDeep(customPaper, "Paper Swatch")?.GetComponent<Image>();
            Require(paperSwatch != null && paperSwatch.sprite != null && paperSwatch.type == Image.Type.Sliced,
                "Paper-colour control should show a sliced paper swatch instead of text.");
            Require(FindDeep(customPaper, "Note Tape") == null &&
                FindDeep(customPaper, "Note Paper Clip") == null &&
                FindDeep(customPaper, "Note Thumbtack") == null,
                "Custom Classroom should not create tape, clips, or thumbtacks.");
            Require(customPaper.GetComponent<Shadow>() != null,
                "Custom Classroom paper should have a soft shadow.");

            RectTransform topicInput = FindDeep(customPaper, "Topic Input") as RectTransform;
            RectTransform materialInput = FindDeep(customPaper, "Material Input") as RectTransform;
            Require(topicInput != null && topicInput.anchoredPosition.x >= 24f,
                "Topic input needs at least 24 px of left margin.");
            Require(materialInput != null && materialInput.anchoredPosition.x >= 24f,
                "Material input needs at least 24 px of left margin.");
            RectTransform gradeControl = FindDeep(customPaper, "Grade Down") as RectTransform;
            Require(topicInput != null && topicInput.sizeDelta.x >= 900f &&
                gradeControl != null && gradeControl.anchoredPosition.y < topicInput.anchoredPosition.y,
                "The custom form should place its settings on a separate row below the full-width topic field.");
            Button blankCreateButton = FindDeep(customPaper, "Create")?.GetComponent<Button>();
            Require(blankCreateButton != null && !blankCreateButton.interactable,
                "Create Classroom should stay disabled until a topic or material is provided.");
            TMP_InputField topicField = topicInput != null ? topicInput.GetComponent<TMP_InputField>() : null;
            Require(topicField != null, "The custom classroom topic field is missing.");
            topicField.text = "Water cycle";
            Require(blankCreateButton.interactable,
                "Create Classroom should activate as soon as the form has a usable topic.");
            Require(FindDeep(customPaper, "Form Status") != null,
                "The custom form should have a dedicated upload/create feedback strip.");
            Require(FindDeep(createOwnButton, "Role Accent") != null &&
                FindDeep(FindDeep(customPaper, "Back"), "Role Accent") != null,
                "Primary and navigation buttons should have distinct role accents.");
            RequireMinimumButtonTargets(classroomObject.transform, 44f);

            EvaluationReportView.Show(
                null,
                new List<FleePupilSession>(),
                null,
                "AraBOT's Class Report",
                "Students helped",
                "Answer success",
                "Teacher's note",
                "Return to Main Menu",
                null);
            reportObject = GameObject.Find("AraBOT Evaluation Report");
            Require(reportObject != null, "Evaluation report was not created.");
            Canvas.ForceUpdateCanvases();
            Transform reportPaper = FindDeep(reportObject.transform, "Paper");
            Require(reportPaper != null && reportPaper.GetComponent<Image>()?.sprite != null,
                "Evaluation report did not receive a paper sprite.");
            Require(CountNamedChildren(reportPaper, "Ruled Line ") >= 15,
                "Evaluation report did not receive enough ruled lines.");
            Require(FindDeep(reportPaper, "Tape") == null &&
                FindDeep(reportPaper, "Paper Clip") == null &&
                FindDeep(reportPaper, "Thumbtack") == null,
                "Evaluation report should not create tape, clips, or thumbtacks.");
            Transform reportSummary = FindDeep(reportPaper, "Summary");
            Transform reportDetails = FindDeep(reportPaper, "Details Viewport");
            Transform reportReturn = FindDeep(reportPaper, "Return Button");
            Require(reportSummary?.GetComponent<Shadow>() != null &&
                reportDetails?.GetComponent<Shadow>() != null,
                "Teacher report paper tiles should have soft shadows.");
            RequireSlicedPaper(reportReturn, false, "Teacher report return button");
            RectTransform detailsText = FindDeep(reportPaper, "Details") as RectTransform;
            Require(detailsText != null && detailsText.sizeDelta.x <= -64f,
                "Teacher report details need safe horizontal padding.");
            RequireMinimumButtonTargets(reportObject.transform, 44f);

            Debug.Log("Paper UI validation passed: hierarchy, responsive spacing, feedback, unclipped text, and touch targets are valid.");
        }
        finally
        {
            if (reportObject != null)
            {
                UnityEngine.Object.DestroyImmediate(reportObject);
            }

            if (classroomObject != null)
            {
                UnityEngine.Object.DestroyImmediate(classroomObject);
            }

            UnityEngine.Object.DestroyImmediate(canvasObject);
        }
    }

    [MenuItem("Tools/Flee the Faculty/Capture Paper UI Previews")]
    public static void CapturePreviews()
    {
        const string preparedPreviewPath = "/private/tmp/flee-classroom-prepared-ui.png";
        const string createPreviewPath = "/private/tmp/flee-classroom-create-ui.png";
        const string savedPreviewPath = "/private/tmp/flee-classroom-saved-ui.png";
        const string reportPreviewPath = "/private/tmp/flee-teacher-report-ui.png";
        const string savedClassroomsKey = "Flee.SavedCustomClassrooms.v1";

        bool hadSavedClassrooms = PlayerPrefs.HasKey(savedClassroomsKey);
        string originalSavedClassrooms = PlayerPrefs.GetString(savedClassroomsKey, string.Empty);
        PlayerPrefs.DeleteKey(savedClassroomsKey);
        SavedClassroomStore.Save(
            string.Empty,
            "Parts of a Plant",
            "Roots absorb water. Stems support the plant. Leaves make food using sunlight.",
            string.Empty,
            3,
            FleeClassroomLanguage.English,
            "Use familiar garden examples.",
            6,
            6);
        SavedClassroomStore.Save(
            string.Empty,
            "Mga Uri ng Hayop",
            "Ang mga hayop ay maaaring mammal, ibon, isda, reptilya, o amphibian.",
            string.Empty,
            4,
            FleeClassroomLanguage.Filipino,
            "Bigyan ng pagkakataong sumagot sa Filipino.",
            5,
            5);
        SavedClassroomStore.Save(
            string.Empty,
            "The Water Cycle",
            "Water evaporates, condenses into clouds, and returns as precipitation.",
            string.Empty,
            5,
            FleeClassroomLanguage.English,
            "Connect each stage to weather the class has observed.",
            7,
            7);
        PlayerPrefs.Save();

        GameObject cameraObject = new GameObject("Paper UI Preview Camera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.055f, 0.08f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        RenderTexture target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        GameObject canvasObject = null;
        GameObject classroomObject = null;
        GameObject reportObject = null;
        try
        {
            canvasObject = new GameObject(
                "Paper UI Preview Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);

            GameObject classroomPrefab = Resources.Load<GameObject>("Classroom Selection");
            classroomObject = UnityEngine.Object.Instantiate(classroomPrefab, canvasObject.transform);
            ClassroomSelectionController controller = classroomObject.GetComponent<ClassroomSelectionController>();
            controller.Configure(canvasObject.transform as RectTransform);

            controller.Open();
            Canvas.ForceUpdateCanvases();
            Capture(camera, target, preparedPreviewPath);

            controller.OpenCustom();
            Canvas.ForceUpdateCanvases();
            Capture(camera, target, createPreviewPath);

            Transform savedButtonTransform = FindDeep(classroomObject.transform, "My Classrooms");
            Button savedButton = savedButtonTransform != null
                ? savedButtonTransform.GetComponent<Button>()
                : null;
            Require(savedButton != null, "My Classrooms button was not found for the preview.");
            savedButton.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            Transform savedPaper = FindDeep(classroomObject.transform, "Classroom Paper");
            Require(savedPaper?.GetComponent<Button>() != null,
                "The saved-classroom paper should be the large mobile-friendly Open target.");
            Require(FindDeep(FindDeep(classroomObject.transform, "Saved Classrooms"), "Enter") == null,
                "Saved classrooms should not keep a separate small Enter button.");
            Capture(camera, target, savedPreviewPath);

            canvasObject.SetActive(false);

            List<FleePupilSession> pupils = new List<FleePupilSession>
            {
                new FleePupilSession(
                    "mary",
                    "Mary",
                    "curious",
                    string.Empty,
                    string.Empty,
                    "Plants get all of their food from the soil.",
                    3,
                    2,
                    true,
                    "Leaves use sunlight, water, and carbon dioxide to make food for the plant.",
                    94),
                new FleePupilSession(
                    "jan",
                    "Jan",
                    "confident",
                    string.Empty,
                    string.Empty,
                    "Only sunlight is needed for photosynthesis.",
                    3,
                    3,
                    true,
                    "Photosynthesis needs sunlight, water, and carbon dioxide.",
                    86),
                new FleePupilSession(
                    "megan",
                    "Megan",
                    "shy",
                    string.Empty,
                    string.Empty,
                    "Oxygen is the gas plants use to make food.",
                    3,
                    3,
                    false,
                    string.Empty,
                    58)
            };
            FleeClassroomSession classroom = new FleeClassroomSession(
                "paper-ui-preview",
                "Photosynthesis",
                2,
                pupils.ToArray(),
                FleeClassroomLanguage.English);
            FleeTeacherSceneResult teacherResult = new FleeTeacherSceneResult(
                new[]
                {
                    new FleeTeacherPupilResult(
                        "megan",
                        "Megan",
                        "Oxygen is the gas plants use to make food.",
                        string.Empty,
                        "Which gas do plants take in to make food?",
                        "Plants take in oxygen.",
                        false)
                },
                2,
                2,
                true,
                "AraBOT helped the class explain photosynthesis clearly. Review the role of carbon dioxide with Megan.",
                "The class understood the ingredients of photosynthesis, but one learner still confused oxygen and carbon dioxide.");
            EvaluationReportView.Show(
                classroom,
                pupils,
                teacherResult,
                "AraBOT's Class Report",
                "Students helped",
                "Answer success",
                "Teacher's note",
                "Return to Main Menu",
                null);
            reportObject = GameObject.Find("AraBOT Evaluation Report");
            Canvas reportCanvas = reportObject != null ? reportObject.GetComponent<Canvas>() : null;
            Require(reportCanvas != null, "Evaluation report canvas was not created.");
            reportCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            reportCanvas.worldCamera = camera;
            reportCanvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            TMP_Text previewDetails = FindDeep(reportObject.transform, "Details")?.GetComponent<TMP_Text>();
            Require(previewDetails != null &&
                previewDetails.text.Contains("●") &&
                previewDetails.text.Contains("NEEDS HELP") &&
                previewDetails.text.Contains("Why:") &&
                previewDetails.text.Contains("Oxygen is the gas plants use to make food."),
                "Teacher report pupil rows should include status labels and grounded failure reasons.");
            previewDetails.ForceMeshUpdate();
            RectTransform previewViewport = previewDetails.transform.parent as RectTransform;
            Require(previewViewport != null && previewDetails.preferredHeight + 30f <= previewViewport.rect.height,
                "The standard teacher report preview should fit without clipping any text.");
            Capture(camera, target, reportPreviewPath);

            Debug.Log(
                "Paper UI screenshots saved to " + preparedPreviewPath + ", " + createPreviewPath + ", " +
                savedPreviewPath + ", and " + reportPreviewPath + ".");
        }
        finally
        {
            RenderTexture.active = null;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);

            if (reportObject != null)
            {
                UnityEngine.Object.DestroyImmediate(reportObject);
            }

            if (classroomObject != null)
            {
                UnityEngine.Object.DestroyImmediate(classroomObject);
            }

            if (canvasObject != null)
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }

            UnityEngine.Object.DestroyImmediate(cameraObject);

            if (hadSavedClassrooms)
            {
                PlayerPrefs.SetString(savedClassroomsKey, originalSavedClassrooms);
            }
            else
            {
                PlayerPrefs.DeleteKey(savedClassroomsKey);
            }

            PlayerPrefs.Save();
        }
    }

    [MenuItem("Tools/Flee the Faculty/Capture Classroom UI Animation")]
    public static void CaptureClassroomAnimation()
    {
        const string frameDirectory = "/private/tmp/flee-classroom-animation-frames";
        const string savedClassroomsKey = "Flee.SavedCustomClassrooms.v1";
        const int holdFrames = 22;
        const int transitionFrames = 10;
        const int headerFadeFrames = 4;
        const float slideDistance = 900f;

        if (Directory.Exists(frameDirectory))
        {
            Directory.Delete(frameDirectory, true);
        }

        Directory.CreateDirectory(frameDirectory);
        bool hadSavedClassrooms = PlayerPrefs.HasKey(savedClassroomsKey);
        string originalSavedClassrooms = PlayerPrefs.GetString(savedClassroomsKey, string.Empty);
        PlayerPrefs.DeleteKey(savedClassroomsKey);
        SavedClassroomStore.Save(
            string.Empty,
            "The Water Cycle",
            "Water evaporates, condenses into clouds, and returns as precipitation.",
            string.Empty,
            5,
            FleeClassroomLanguage.English,
            "Connect each stage to familiar weather.",
            7,
            7);
        PlayerPrefs.Save();

        GameObject cameraObject = new GameObject("Classroom Animation Camera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.055f, 0.08f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        RenderTexture target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        GameObject canvasObject = null;
        GameObject classroomObject = null;
        try
        {
            canvasObject = new GameObject(
                "Classroom Animation Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);

            GameObject classroomPrefab = Resources.Load<GameObject>("Classroom Selection");
            classroomObject = UnityEngine.Object.Instantiate(classroomPrefab, canvasObject.transform);
            ClassroomSelectionController controller = classroomObject.GetComponent<ClassroomSelectionController>();
            controller.Configure(canvasObject.transform as RectTransform);

            int frame = 0;
            controller.Open();
            Canvas.ForceUpdateCanvases();
            SetPreviewHeaderAlpha(classroomObject, 0f);
            frame = CapturePanelEnter(
                camera,
                target,
                frameDirectory,
                frame,
                FindDeep(classroomObject.transform, "Prepared Classrooms"),
                transitionFrames,
                slideDistance);
            frame = CaptureHeaderFade(
                camera,
                target,
                frameDirectory,
                frame,
                classroomObject,
                headerFadeFrames);
            frame = CaptureHold(camera, target, frameDirectory, frame, holdFrames);

            SetPreviewHeaderAlpha(classroomObject, 0f);
            frame = CapturePanelExit(
                camera,
                target,
                frameDirectory,
                frame,
                FindDeep(classroomObject.transform, "Prepared Classrooms"),
                transitionFrames,
                slideDistance);
            controller.OpenCustom();
            Canvas.ForceUpdateCanvases();
            frame = CapturePanelEnter(
                camera,
                target,
                frameDirectory,
                frame,
                FindDeep(classroomObject.transform, "Custom Classroom"),
                transitionFrames,
                slideDistance);
            frame = CaptureHold(camera, target, frameDirectory, frame, holdFrames + 8);

            frame = CapturePanelExit(
                camera,
                target,
                frameDirectory,
                frame,
                FindDeep(classroomObject.transform, "Custom Classroom"),
                transitionFrames,
                slideDistance);
            controller.Open();
            Canvas.ForceUpdateCanvases();
            SetPreviewHeaderAlpha(classroomObject, 0f);
            frame = CapturePanelEnter(
                camera,
                target,
                frameDirectory,
                frame,
                FindDeep(classroomObject.transform, "Prepared Classrooms"),
                transitionFrames,
                slideDistance);
            frame = CaptureHeaderFade(
                camera,
                target,
                frameDirectory,
                frame,
                classroomObject,
                headerFadeFrames);
            frame = CaptureHold(camera, target, frameDirectory, frame, 14);

            SetPreviewHeaderAlpha(classroomObject, 0f);
            frame = CapturePanelExit(
                camera,
                target,
                frameDirectory,
                frame,
                FindDeep(classroomObject.transform, "Prepared Classrooms"),
                transitionFrames,
                slideDistance);
            Button savedButton = FindDeep(classroomObject.transform, "My Classrooms")?.GetComponent<Button>();
            Require(savedButton != null, "My Classrooms button was not found for the animation preview.");
            savedButton.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            SetPreviewHeaderAlpha(classroomObject, 0f);
            frame = CapturePanelEnter(
                camera,
                target,
                frameDirectory,
                frame,
                FindDeep(classroomObject.transform, "Saved Classrooms"),
                transitionFrames,
                slideDistance);
            frame = CaptureHeaderFade(
                camera,
                target,
                frameDirectory,
                frame,
                classroomObject,
                headerFadeFrames);
            frame = CaptureHold(camera, target, frameDirectory, frame, holdFrames + 8);
            frame = CapturePanelExit(
                camera,
                target,
                frameDirectory,
                frame,
                FindDeep(classroomObject.transform, "Classroom Window"),
                transitionFrames,
                slideDistance);
            classroomObject.SetActive(false);
            CaptureHold(camera, target, frameDirectory, frame, 10);

            Debug.Log("Classroom UI animation frames saved to " + frameDirectory + ".");
        }
        finally
        {
            RenderTexture.active = null;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);

            if (classroomObject != null)
            {
                UnityEngine.Object.DestroyImmediate(classroomObject);
            }

            if (canvasObject != null)
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }

            UnityEngine.Object.DestroyImmediate(cameraObject);
            if (hadSavedClassrooms)
            {
                PlayerPrefs.SetString(savedClassroomsKey, originalSavedClassrooms);
            }
            else
            {
                PlayerPrefs.DeleteKey(savedClassroomsKey);
            }

            PlayerPrefs.Save();
        }
    }

    private static int CapturePanelEnter(
        Camera camera,
        RenderTexture target,
        string directory,
        int firstFrame,
        Transform panel,
        int frameCount,
        float slideDistance)
    {
        Require(panel != null, "The classroom panel was not found for the animation preview.");
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = panel.gameObject.AddComponent<CanvasGroup>();
        }

        AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        group.alpha = 1f;
        for (int index = 0; index < frameCount; index++)
        {
            float progress = frameCount > 1 ? index / (float)(frameCount - 1) : 1f;
            float eased = curve.Evaluate(progress);
            RectTransform rect = panel as RectTransform;
            rect.anchoredPosition = Vector2.down * (slideDistance * (1f - eased));
            Canvas.ForceUpdateCanvases();
            Capture(camera, target, FramePath(directory, firstFrame + index));
        }

        group.alpha = 1f;
        (panel as RectTransform).anchoredPosition = Vector2.zero;
        return firstFrame + frameCount;
    }

    private static int CapturePanelExit(
        Camera camera,
        RenderTexture target,
        string directory,
        int firstFrame,
        Transform panel,
        int frameCount,
        float slideDistance)
    {
        Require(panel != null, "The classroom panel was not found for the animation preview.");
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = panel.gameObject.AddComponent<CanvasGroup>();
        }

        AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        group.alpha = 1f;
        for (int index = 0; index < frameCount; index++)
        {
            float progress = frameCount > 1 ? index / (float)(frameCount - 1) : 1f;
            float eased = curve.Evaluate(progress);
            RectTransform rect = panel as RectTransform;
            rect.anchoredPosition = Vector2.down * (slideDistance * eased);
            Canvas.ForceUpdateCanvases();
            Capture(camera, target, FramePath(directory, firstFrame + index));
        }

        group.alpha = 1f;
        (panel as RectTransform).anchoredPosition = Vector2.zero;
        return firstFrame + frameCount;
    }

    private static int CaptureHold(
        Camera camera,
        RenderTexture target,
        string directory,
        int firstFrame,
        int frameCount)
    {
        for (int index = 0; index < frameCount; index++)
        {
            Capture(camera, target, FramePath(directory, firstFrame + index));
        }

        return firstFrame + frameCount;
    }

    private static int CaptureHeaderFade(
        Camera camera,
        RenderTexture target,
        string directory,
        int firstFrame,
        GameObject classroomObject,
        int frameCount)
    {
        Transform header = FindDeep(classroomObject.transform, "Shared Header");
        Require(header != null, "The shared classroom header was not found for the animation preview.");
        CanvasGroup group = header.GetComponent<CanvasGroup>();
        Require(group != null, "The shared classroom header is missing its CanvasGroup.");
        header.gameObject.SetActive(true);

        for (int index = 0; index < frameCount; index++)
        {
            float progress = frameCount > 1 ? index / (float)(frameCount - 1) : 1f;
            group.alpha = progress;
            Capture(camera, target, FramePath(directory, firstFrame + index));
        }

        group.alpha = 1f;
        return firstFrame + frameCount;
    }

    private static void SetPreviewHeaderAlpha(GameObject classroomObject, float alpha)
    {
        Transform header = FindDeep(classroomObject.transform, "Shared Header");
        if (header == null)
        {
            return;
        }

        CanvasGroup group = header.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = Mathf.Clamp01(alpha);
        }
    }

    private static string FramePath(string directory, int frame)
    {
        return Path.Combine(directory, "frame-" + frame.ToString("D4") + ".png");
    }

    private static SpriteMetaData Sprite(
        string name,
        float x,
        float yFromTop,
        float width,
        float height,
        float border = 0f)
    {
        return new SpriteMetaData
        {
            name = name,
            rect = new Rect(x, SourceHeight - yFromTop - height, width, height),
            alignment = (int)SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f),
            border = new Vector4(border, border, border, border)
        };
    }

    private static Sprite Get(IReadOnlyDictionary<string, Sprite> sprites, string name)
    {
        sprites.TryGetValue(name, out Sprite sprite);
        return sprite;
    }

    private static Sprite[] GetSeries(IReadOnlyDictionary<string, Sprite> sprites, string prefix)
    {
        string[] names = { "Aqua", "Blue", "Purple", "Violet", "Pink", "Green", "Mint", "Yellow", "Orange", "Red" };
        Sprite[] result = new Sprite[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            result[index] = Get(sprites, prefix + names[index]);
        }

        return result;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent == null)
        {
            return null;
        }

        if (string.Equals(parent.name, name, StringComparison.Ordinal))
        {
            return parent;
        }

        for (int index = 0; index < parent.childCount; index++)
        {
            Transform result = FindDeep(parent.GetChild(index), name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static int CountNamedChildren(Transform parent, string prefix)
    {
        int count = 0;
        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child.name.StartsWith(prefix, StringComparison.Ordinal))
            {
                count++;
            }

            count += CountNamedChildren(child, prefix);
        }

        return count;
    }

    private static bool HasSliceBorder(Sprite sprite)
    {
        if (sprite == null)
        {
            return false;
        }

        Vector4 border = sprite.border;
        return border.x > 0f && border.y > 0f && border.z > 0f && border.w > 0f;
    }

    private static void RequireSlicedPaper(Transform target, bool rounded, string label)
    {
        Image image = target != null ? target.GetComponent<Image>() : null;
        Require(image != null && image.sprite != null && image.type == Image.Type.Sliced,
            label + " should use a 9-sliced paper sprite.");
        bool isRounded = image.sprite.name.IndexOf("Rounded", StringComparison.OrdinalIgnoreCase) >= 0;
        Require(isRounded == rounded,
            label + (rounded
                ? " should use the rounded tile sprite."
                : " should use the rigid square sprite."));
    }

    private static void RequireMinimumButtonTargets(Transform root, float minimumSize)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            RectTransform rect = buttons[index].transform as RectTransform;
            Vector2 size = rect != null ? rect.rect.size : Vector2.zero;
            Require(size.x >= minimumSize && size.y >= minimumSize,
                "Button '" + buttons[index].name + "' is smaller than " + minimumSize + " px.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        RenderTexture previous = RenderTexture.active;
        camera.Render();
        RenderTexture.active = target;
        Texture2D screenshot = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        screenshot.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
        screenshot.Apply();
        File.WriteAllBytes(path, screenshot.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(screenshot);
        RenderTexture.active = previous;
    }
}
#endif

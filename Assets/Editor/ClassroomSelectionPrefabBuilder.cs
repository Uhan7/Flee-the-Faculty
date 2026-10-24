using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ClassroomSelectionPrefabBuilder
{
    private const string PrefabPath = "Assets/Resources/Classroom Selection.prefab";
    private const string ViewFolderPath = "Assets/Resources/Classroom Selection Views";
    private const string ClassroomHomePrefabPath = ViewFolderPath + "/Classroom Home.prefab";
    private const string SampleClassroomsPrefabPath = ViewFolderPath + "/Sample Classrooms.prefab";
    private const string CustomClassroomPrefabPath = ViewFolderPath + "/Custom Classroom.prefab";
    private const string SavedClassroomsPrefabPath = ViewFolderPath + "/Saved Classrooms.prefab";

    static ClassroomSelectionPrefabBuilder()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
        {
            return;
        }

        // Also supports projects using fast enter play mode with scene reloads
        // disabled, where runtime scene-loaded callbacks are intentionally skipped.
        EditorApplication.delayCall += () =>
        {
            MainMenuController mainMenu = Object.FindFirstObjectByType<MainMenuController>();
            ClassroomSelectionBootstrap.EnsureFor(mainMenu);
        };
    }

    [MenuItem("Flee the Faculty/Rebuild Classroom Selection Prefab")]
    public static void Build()
    {
        GameObject root = CreateBuildRoot(out RectTransform rect);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

        EnsureViewFolder();
        ClassroomSelectionController controller = root.GetComponent<ClassroomSelectionController>();
        controller.ConfigureForPrefabBuild(rect);
        SaveViewPrefab(root.transform, "Classroom Home", ClassroomHomePrefabPath, "Create A Classroom");
        SaveViewPrefab(root.transform, "Sample Classrooms", SampleClassroomsPrefabPath, "PHOTOSYNTHESIS");
        SaveViewPrefab(root.transform, "Custom Classroom", CustomClassroomPrefabPath, "Topic Input");
        SaveViewPrefab(root.transform, "Saved Classrooms", SavedClassroomsPrefabPath, "Saved Classroom Scroll View");

        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Built " + PrefabPath + " and its editable classroom view prefabs.");
    }

    [MenuItem("Flee the Faculty/Rebuild Sample Classrooms Prefab")]
    public static void RebuildSampleClassrooms()
    {
        GameObject root = CreateBuildRoot(out RectTransform rect);
        ClassroomSelectionController controller = root.GetComponent<ClassroomSelectionController>();
        controller.ConfigureForPrefabBuild(rect);
        SaveViewPrefab(
            root.transform,
            "Sample Classrooms",
            SampleClassroomsPrefabPath,
            "PHOTOSYNTHESIS",
            true);
        Object.DestroyImmediate(root);
        NormalizeViewPrefabColours(SampleClassroomsPrefabPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Rebuilt " + SampleClassroomsPrefabPath);
    }

    public static void BuildBatch()
    {
        Build();
    }

    public static void RepairAndNormalizeBatch()
    {
        Build();
        NormalizeViewPrefabColours();
    }

    [MenuItem("Flee the Faculty/Apply UIUX Layout Updates")]
    public static void ApplyUiUxUpdates()
    {
        GameObject root = CreateBuildRoot(out RectTransform rect);
        EnsureViewFolder();
        root.GetComponent<ClassroomSelectionController>().ConfigureForPrefabBuild(rect);
        SaveViewPrefab(root.transform, "Classroom Home", ClassroomHomePrefabPath, "Create A Classroom", true);
        SaveViewPrefab(root.transform, "Sample Classrooms", SampleClassroomsPrefabPath, "PHOTOSYNTHESIS", true);
        SaveViewPrefab(root.transform, "Custom Classroom", CustomClassroomPrefabPath, "Topic Input", true);
        SaveViewPrefab(root.transform, "Saved Classrooms", SavedClassroomsPrefabPath, "Saved Classroom Scroll View", true);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("UIUX classroom layouts updated.");
    }

    public static void ApplyUiUxUpdatesAndPreview()
    {
        ApplyUiUxUpdates();
        PaperUiAssetSetup.CapturePreviews();
    }

    [MenuItem("Flee the Faculty/Preview Classroom Selection", true)]
    private static bool CanPreview()
    {
        return Application.isPlaying;
    }

    [MenuItem("Flee the Faculty/Preview Classroom Selection")]
    private static void Preview()
    {
        ClassroomSelectionController[] controllers = Object.FindObjectsByType<ClassroomSelectionController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        ClassroomSelectionController controller = controllers.Length > 0 ? controllers[0] : null;
        if (controller == null)
        {
            Debug.LogError("The classroom selector is not present in the active scene.");
            return;
        }

        RefreshPreviewInterface(controller);
        controller.Open();
    }

    [MenuItem("Flee the Faculty/Preview Custom Classroom", true)]
    private static bool CanPreviewCustom()
    {
        return Application.isPlaying;
    }

    [MenuItem("Flee the Faculty/Preview Custom Classroom")]
    private static void PreviewCustom()
    {
        ClassroomSelectionController[] controllers = Object.FindObjectsByType<ClassroomSelectionController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (controllers.Length == 0)
        {
            Debug.LogError("The classroom selector is not present in the active scene.");
            return;
        }

        RefreshPreviewInterface(controllers[0]);
        controllers[0].OpenCustom();
    }

    [MenuItem("Flee the Faculty/Preview Saved Classrooms", true)]
    private static bool CanPreviewSaved()
    {
        return Application.isPlaying;
    }

    [MenuItem("Flee the Faculty/Preview Saved Classrooms")]
    private static void PreviewSaved()
    {
        ClassroomSelectionController[] controllers = Object.FindObjectsByType<ClassroomSelectionController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (controllers.Length == 0)
        {
            Debug.LogError("The classroom selector is not present in the active scene.");
            return;
        }

        RefreshPreviewInterface(controllers[0]);
        controllers[0].OpenSaved();
    }

    [MenuItem("Flee the Faculty/Preview Sample Classrooms", true)]
    private static bool CanPreviewSamples()
    {
        return Application.isPlaying;
    }

    [MenuItem("Flee the Faculty/Preview Sample Classrooms")]
    private static void PreviewSamples()
    {
        ClassroomSelectionController[] controllers = Object.FindObjectsByType<ClassroomSelectionController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (controllers.Length == 0)
        {
            Debug.LogError("The classroom selector is not present in the active scene.");
            return;
        }

        RefreshPreviewInterface(controllers[0]);
        controllers[0].OpenSamples();
    }

    private static void RefreshPreviewInterface(ClassroomSelectionController controller)
    {
        RectTransform parent = controller.transform.parent as RectTransform;
        for (int index = controller.transform.childCount - 1; index >= 0; index--)
        {
            Object.DestroyImmediate(controller.transform.GetChild(index).gameObject);
        }

        controller.Configure(parent);
    }

    private static void EnsureViewFolder()
    {
        if (!AssetDatabase.IsValidFolder(ViewFolderPath))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Classroom Selection Views");
        }
    }

    [MenuItem("Flee the Faculty/Normalize Classroom Selection Prefab Colours")]
    public static void NormalizeViewPrefabColours()
    {
        NormalizeViewPrefabColours(ClassroomHomePrefabPath);
        NormalizeViewPrefabColours(SampleClassroomsPrefabPath);
        NormalizeViewPrefabColours(CustomClassroomPrefabPath);
        NormalizeViewPrefabColours(SavedClassroomsPrefabPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void SaveViewPrefab(
        Transform root,
        string viewName,
        string assetPath,
        string requiredChildName,
        bool forceReplace = false)
    {
        Transform classroomWindow = FindDescendant(root, "Classroom Window");
        Transform view = classroomWindow != null
            ? classroomWindow.Find(viewName)
            : null;
        if (view == null)
        {
            Debug.LogError("Could not build classroom view prefab: " + viewName);
            return;
        }

        // These become designer-owned assets after their first generation.
        // Rebuilding the bootstrap prefab must not erase manual prefab edits. An
        // invalid generated prefab is repaired automatically, though.
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        bool existingPrefabIsValid = existingPrefab != null
            && FindDescendant(existingPrefab.transform, requiredChildName) != null;
        if (existingPrefabIsValid && !forceReplace)
        {
            Debug.Log("Kept existing editable classroom view prefab: " + assetPath);
            return;
        }

        Transform originalParent = view.parent;
        int siblingIndex = view.GetSiblingIndex();
        view.SetParent(null, false);
        view.gameObject.SetActive(true);
        PrefabUtility.SaveAsPrefabAsset(view.gameObject, assetPath);
        view.SetParent(originalParent, false);
        view.SetSiblingIndex(siblingIndex);

        if (existingPrefab != null)
        {
            Debug.Log("Repaired invalid classroom view prefab: " + assetPath);
        }
    }

    private static GameObject CreateBuildRoot(out RectTransform rect)
    {
        GameObject root = new GameObject(
            "Classroom Selection",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(ClassroomSelectionController));
        root.layer = 5;

        rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image rootImage = root.GetComponent<Image>();
        rootImage.color = Color.clear;
        rootImage.raycastTarget = false;
        return root;
    }

    private static void NormalizeViewPrefabColours(string assetPath)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) == null)
        {
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
        Image[] images = root.GetComponentsInChildren<Image>(true);
        for (int index = 0; index < images.Length; index++)
        {
            Color current = images[index].color;
            images[index].color = new Color(1f, 1f, 1f, current.a);
        }

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            buttons[index].transition = Selectable.Transition.None;
        }

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = transforms.Length - 1; index >= 0; index--)
        {
            if (transforms[index] != root.transform && transforms[index].name == "Role Accent")
            {
                Object.DestroyImmediate(transforms[index].gameObject);
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, assetPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static Transform FindDescendant(Transform parent, string targetName)
    {
        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child.name == targetName)
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
}

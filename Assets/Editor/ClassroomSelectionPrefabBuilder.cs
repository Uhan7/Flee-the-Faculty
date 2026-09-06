using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ClassroomSelectionPrefabBuilder
{
    private const string PrefabPath = "Assets/Resources/Classroom Selection.prefab";

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
        GameObject root = new GameObject(
            "Classroom Selection",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(ClassroomSelectionController));
        root.layer = 5;

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image rootImage = root.GetComponent<Image>();
        rootImage.color = Color.clear;
        rootImage.raycastTarget = false;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Built " + PrefabPath);
    }

    public static void BuildBatch()
    {
        Build();
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

    private static void RefreshPreviewInterface(ClassroomSelectionController controller)
    {
        RectTransform parent = controller.transform.parent as RectTransform;
        for (int index = controller.transform.childCount - 1; index >= 0; index--)
        {
            Object.DestroyImmediate(controller.transform.GetChild(index).gameObject);
        }

        controller.Configure(parent);
    }
}

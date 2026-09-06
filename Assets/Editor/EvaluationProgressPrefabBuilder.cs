using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class EvaluationProgressPrefabBuilder
{
    private const int SlotCount = 8;
    private const string HudPrefabPath = "Assets/Prefabs/Evaluation Progress HUD.prefab";
    private const string ControllerPrefabPath = "Assets/Resources/Teacher Evaluation Sequence.prefab";
    private const string EvaluationScenePath = "Assets/Scenes/Teacher Evaluation.unity";
    private const string IconsPath = "Assets/Sprites/UI Stuff/More Icons.png";
    private const string FontPath = "Assets/Fonts/TMP/Octosale TMP SDF.asset";

    [MenuItem("Flee the Faculty/Rebuild Evaluation Progress HUD")]
    public static void Build()
    {
        Sprite studentSprite = LoadSprite("More Icons_9");
        Sprite passedSprite = LoadSprite("More Icons_10");
        Sprite failedSprite = LoadSprite("More Icons_11");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        GameObject root = new GameObject(
            "Evaluation Progress HUD",
            typeof(RectTransform),
            typeof(EvaluationProgressView));
        root.layer = 5;

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(18f, -14f);
        rootRect.sizeDelta = new Vector2(480f, 116f);

        GameObject[] slots = new GameObject[SlotCount];
        GameObject[] passedMarks = new GameObject[SlotCount];
        GameObject[] failedMarks = new GameObject[SlotCount];

        for (int index = 0; index < SlotCount; index++)
        {
            RectTransform slot = CreateRect(
                rootRect,
                "Student " + (index + 1),
                new Vector2(54f, 90f),
                new Vector2(27f + index * 58f, -45f));
            slot.anchorMin = slot.anchorMax = slot.pivot = new Vector2(0f, 1f);
            slots[index] = slot.gameObject;

            CreateImage(
                slot,
                "Full Uniform",
                studentSprite,
                new Vector2(34f, 70f),
                new Vector2(0f, -9f));
            passedMarks[index] = CreateImage(
                slot,
                "Passed Check",
                passedSprite,
                new Vector2(31f, 24f),
                new Vector2(0f, 28f)).gameObject;
            failedMarks[index] = CreateImage(
                slot,
                "Failed X",
                failedSprite,
                new Vector2(26f, 26f),
                new Vector2(0f, 28f)).gameObject;
            passedMarks[index].SetActive(false);
            failedMarks[index].SetActive(false);
        }

        TMP_Text caption = CreateText(
            rootRect,
            "Assessed Count",
            font,
            18f,
            new Vector2(460f, 24f),
            new Vector2(230f, -103f));
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.text = "0 / 0 assessed";

        SerializedObject view = new SerializedObject(root.GetComponent<EvaluationProgressView>());
        AssignArray(view.FindProperty("studentSlots"), slots);
        AssignArray(view.FindProperty("passedMarks"), passedMarks);
        AssignArray(view.FindProperty("failedMarks"), failedMarks);
        view.FindProperty("assessedCountText").objectReferenceValue = caption;
        view.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        InstallIntoEvaluationScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Built and installed " + HudPrefabPath);
    }

    public static void BuildBatch()
    {
        Build();
    }

    private static void InstallIntoEvaluationScene()
    {
        Scene scene = EditorSceneManager.OpenScene(EvaluationScenePath, OpenSceneMode.Single);
        Canvas mainCanvas = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
            .FirstOrDefault(canvas => canvas.name == "Main Canvas");
        if (mainCanvas == null)
        {
            throw new InvalidOperationException("Teacher Evaluation is missing Main Canvas.");
        }

        EvaluationProgressView[] existingViews = mainCanvas.GetComponentsInChildren<EvaluationProgressView>(true);
        for (int index = 0; index < existingViews.Length; index++)
        {
            UnityEngine.Object.DestroyImmediate(existingViews[index].gameObject);
        }

        GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
        GameObject hud = PrefabUtility.InstantiatePrefab(hudPrefab, scene) as GameObject;
        hud.transform.SetParent(mainCanvas.transform, false);
        hud.transform.SetAsFirstSibling();

        TeacherEvaluationController controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<TeacherEvaluationController>(true))
            .FirstOrDefault();
        if (controller == null)
        {
            GameObject controllerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ControllerPrefabPath);
            GameObject controllerObject = PrefabUtility.InstantiatePrefab(controllerPrefab, scene) as GameObject;
            controller = controllerObject != null
                ? controllerObject.GetComponent<TeacherEvaluationController>()
                : null;
        }

        if (controller == null)
        {
            throw new InvalidOperationException("Teacher Evaluation is missing its controller.");
        }

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("evaluationHud").objectReferenceValue =
            hud.GetComponent<EvaluationProgressView>();
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static Sprite LoadSprite(string spriteName)
    {
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(IconsPath)
            .OfType<Sprite>()
            .FirstOrDefault(candidate => candidate.name == spriteName);
        if (sprite == null)
        {
            throw new InvalidOperationException("Could not find " + spriteName + " in " + IconsPath + ".");
        }

        return sprite;
    }

    private static RectTransform CreateRect(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 position)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = 5;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static RectTransform CreateImage(
        Transform parent,
        string name,
        Sprite sprite,
        Vector2 size,
        Vector2 position)
    {
        RectTransform rect = CreateRect(parent, name, size, position);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return rect;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string name,
        TMP_FontAsset font,
        float fontSize,
        Vector2 size,
        Vector2 position)
    {
        RectTransform rect = CreateRect(parent, name, size, position);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.color = new Color(0.22f, 0.12f, 0.09f, 1f);
        text.raycastTarget = false;
        return text;
    }

    private static void AssignArray(SerializedProperty property, GameObject[] values)
    {
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++)
        {
            property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }
    }
}

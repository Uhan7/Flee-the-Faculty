using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Build the access code box into the main menu's Settings Panel.
///
/// A menu command rather than hand-placed objects, so the panel can be rebuilt
/// after a change to the layout without anyone remembering which child went
/// where. Running it twice replaces what it made and leaves the rest alone.
/// </summary>
public static class AccessCodeSettingsBuilder
{
    private const string PanelName = "Settings Panel";
    private const string GroupName = "Access Code";
    private const string MenuScenePath = "Assets/Scenes/Main Menu.unity";

    /// <summary>Smallest panel the box fits in without the rows colliding.</summary>
    private static readonly Vector2 PanelSize = new Vector2(620f, 400f);

    [MenuItem("Flee the Faculty/Build The Access Code Settings", priority = 200)]
    public static void Build()
    {
        GameObject panel = Find(PanelName);
        if (panel == null)
        {
            // The panel lives in the main menu, so open it rather than asking.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(MenuScenePath);
            panel = Find(PanelName);
        }

        if (panel == null)
        {
            EditorUtility.DisplayDialog(
                "No Settings Panel",
                $"No object called '{PanelName}' in {MenuScenePath}.",
                "OK");
            return;
        }

        Transform existing = panel.transform.Find(GroupName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        // The panel was sized for the placeholder line it used to hold, which
        // is a third of what this needs. Grow it, and put the placeholder away.
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.sizeDelta = new Vector2(
            Mathf.Max(panelRect.sizeDelta.x, PanelSize.x),
            Mathf.Max(panelRect.sizeDelta.y, PanelSize.y));

        Transform placeholder = panel.transform.Find("Text");
        if (placeholder != null && placeholder.gameObject.activeSelf)
        {
            Undo.RecordObject(placeholder.gameObject, "Hide settings placeholder");
            placeholder.gameObject.SetActive(false);
        }

        GameObject group = NewChild(GroupName, panel.transform);
        Fill(group, 24f);

        VerticalLayoutGroup layout = group.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;

        Label(group.transform, "Heading", "ACCESS CODE", 34f);
        Label(
            group.transform,
            "Explanation",
            "Ask whoever runs the Classroom service for the code, then paste it here.",
            20f);

        TMP_InputField field = Field(group.transform);
        TMP_Text status = Label(group.transform, "Status", string.Empty, 20f);

        GameObject row = NewChild("Buttons", group.transform);
        Size(row, new Vector2(0f, 48f));
        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = false;
        rowLayout.childForceExpandWidth = false;

        Button save = MakeButton(row.transform, "Save", "SAVE");
        Button clear = MakeButton(row.transform, "Clear", "FORGET");

        ClientTokenField wiring = group.AddComponent<ClientTokenField>();
        SerializedObject serialized = new SerializedObject(wiring);
        serialized.FindProperty("input").objectReferenceValue = field;
        serialized.FindProperty("saveButton").objectReferenceValue = save;
        serialized.FindProperty("clearButton").objectReferenceValue = clear;
        serialized.FindProperty("status").objectReferenceValue = status;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(group, "Build access code settings");
        EditorSceneManager.MarkSceneDirty(panel.scene);
        EditorSceneManager.SaveScene(panel.scene);
        Selection.activeGameObject = group;

        Debug.Log($"Built the access code box into {PanelName}.", group);
    }

    private static GameObject Find(string name)
    {
        foreach (GameObject root in
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform found = FindDeep(root.transform, name);
            if (found != null)
            {
                return found.gameObject;
            }
        }

        return null;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name)
        {
            return parent;
        }

        for (int index = 0; index < parent.childCount; index++)
        {
            Transform found = FindDeep(parent.GetChild(index), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static GameObject NewChild(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child;
    }

    /// <summary>Stretch to the parent's edges, inset by the same margin all round.</summary>
    private static void Fill(GameObject target, float margin)
    {
        RectTransform rect = (RectTransform)target.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
    }

    private static void Size(GameObject target, Vector2 size)
    {
        ((RectTransform)target.transform).sizeDelta = size;
    }

    private static TMP_Text Label(Transform parent, string name, string text, float size)
    {
        GameObject host = NewChild(name, parent);
        Size(host, new Vector2(0f, size * 1.6f));
        TextMeshProUGUI label = host.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    private static TMP_InputField Field(Transform parent)
    {
        GameObject host = NewChild("Code", parent);
        Size(host, new Vector2(0f, 52f));
        Image background = host.AddComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.9f);

        GameObject viewport = NewChild("Text Area", host.transform);
        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(12f, 8f);
        viewportRect.offsetMax = new Vector2(-12f, -8f);
        viewport.AddComponent<RectMask2D>();

        GameObject textObject = NewChild("Text", viewport.transform);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = 22f;
        text.color = Color.black;

        TMP_InputField field = host.AddComponent<TMP_InputField>();
        field.textViewport = viewportRect;
        field.textComponent = text;
        field.lineType = TMP_InputField.LineType.SingleLine;
        // The code is a credential, so it is masked while typed and not put
        // back into the box afterwards.
        field.contentType = TMP_InputField.ContentType.Password;
        return field;
    }

    private static Button MakeButton(Transform parent, string name, string caption)
    {
        GameObject host = NewChild(name, parent);
        Size(host, new Vector2(180f, 48f));
        host.AddComponent<Image>();
        Button button = host.AddComponent<Button>();
        TMP_Text label = Label(host.transform, "Label", caption, 22f);
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.color = Color.black;
        return button;
    }
}

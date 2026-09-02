using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the Main Menu's separate Access and audio Settings windows.
/// Running it again refreshes only the objects this builder owns.
/// </summary>
public static class AccessCodeSettingsBuilder
{
    private const string AccessPanelName = "Access Panel";
    private const string SettingsPanelName = "Settings Panel";
    private const string AccessButtonName = "Access Code Button";
    private const string MenuScenePath = "Assets/Scenes/Main Menu.unity";

    private static TMP_FontAsset menuFont;
    private static Sprite buttonSprite;
    private static Sprite panelSprite;

    [MenuItem("Flee the Faculty/Build Main Menu Panels", priority = 200)]
    public static void Build()
    {
        if (EditorSceneManager.GetActiveScene().path != MenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(MenuScenePath);
        }

        MainMenuController controller = Object.FindFirstObjectByType<MainMenuController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog(
                "No Main Menu Controller",
                $"No MainMenuController was found in {MenuScenePath}.",
                "OK");
            return;
        }

        Button settingsButton = Find("Settings Button")?.GetComponent<Button>();
        CaptureStyle(settingsButton);

        GameObject accessPanel = Find(AccessPanelName);
        if (accessPanel == null)
        {
            GameObject legacyPanel = Find(SettingsPanelName);
            if (legacyPanel != null && legacyPanel.GetComponentInChildren<ClientTokenField>(true) != null)
            {
                accessPanel = legacyPanel;
                accessPanel.name = AccessPanelName;
            }
        }

        accessPanel = PreparePanel(accessPanel, AccessPanelName, controller.transform, new Vector2(650f, 440f));
        BuildAccessPanel(accessPanel, controller);

        GameObject settingsPanel = PreparePanel(
            Find(SettingsPanelName),
            SettingsPanelName,
            controller.transform,
            new Vector2(580f, 360f));
        BuildSettingsPanel(settingsPanel, controller);

        if (settingsButton != null)
        {
            SetButtonCaption(settingsButton, "SETTINGS");
            settingsButton.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(settingsButton.onClick, controller.ToggleSettingsPanel);
            EditorUtility.SetDirty(settingsButton);
        }

        Button accessButton = BuildAccessButton(controller.transform, controller, settingsButton);

        SerializedObject controllerData = new SerializedObject(controller);
        controllerData.FindProperty("accessPanel").objectReferenceValue = accessPanel;
        controllerData.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        controllerData.ApplyModifiedPropertiesWithoutUndo();

        accessPanel.SetActive(false);
        settingsPanel.SetActive(false);

        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        EditorSceneManager.SaveScene(controller.gameObject.scene);
        Selection.activeGameObject = accessButton.gameObject;
        Debug.Log("Built separate Access and audio Settings panels on the Main Menu.", controller);
    }

    private static void CaptureStyle(Button settingsButton)
    {
        if (settingsButton != null)
        {
            Image image = settingsButton.GetComponent<Image>();
            buttonSprite = image != null ? image.sprite : null;
            TMP_Text label = settingsButton.GetComponentInChildren<TMP_Text>(true);
            menuFont = label != null ? label.font : null;
        }

        GameObject existingPanel = Find(AccessPanelName);
        Image panelImage = existingPanel != null ? existingPanel.GetComponent<Image>() : null;
        panelSprite = panelImage != null ? panelImage.sprite : null;
    }

    private static GameObject PreparePanel(
        GameObject panel,
        string name,
        Transform parent,
        Vector2 size)
    {
        if (panel == null)
        {
            panel = NewChild(name, parent);
        }

        panel.name = name;
        panel.layer = parent.gameObject.layer;
        ClearChildren(panel.transform);

        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        Image background = panel.GetComponent<Image>();
        if (background == null)
        {
            background = panel.AddComponent<Image>();
        }

        background.sprite = panelSprite;
        background.type = panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        background.color = new Color(0.882f, 0.929f, 0.973f, 0.97f);
        background.raycastTarget = true;
        return panel;
    }

    private static void BuildAccessPanel(GameObject panel, MainMenuController controller)
    {
        GameObject content = NewChild("Access Code", panel.transform);
        Fill(content, 28f);
        VerticalLayoutGroup layout = AddVerticalLayout(content, 12f);
        layout.padding = new RectOffset(18, 18, 14, 14);

        Label(content.transform, "Heading", "ACCESS CODE", 34f, 52f);
        Label(
            content.transform,
            "Explanation",
            "Enter the code supplied by whoever runs the Classroom service.",
            20f,
            64f);

        TMP_InputField field = Field(content.transform);
        TMP_Text status = Label(content.transform, "Status", string.Empty, 19f, 42f);

        GameObject row = NewChild("Buttons", content.transform);
        Size(row, new Vector2(0f, 54f));
        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = false;
        rowLayout.childForceExpandWidth = false;

        Button save = MakeButton(row.transform, "Save", "SAVE", 165f);
        Button clear = MakeButton(row.transform, "Clear", "FORGET", 165f);
        Button close = MakeButton(row.transform, "Close", "CLOSE", 165f);
        UnityEventTools.AddPersistentListener(close.onClick, controller.CloseAccessPanel);

        ClientTokenField wiring = content.AddComponent<ClientTokenField>();
        SerializedObject serialized = new SerializedObject(wiring);
        serialized.FindProperty("input").objectReferenceValue = field;
        serialized.FindProperty("saveButton").objectReferenceValue = save;
        serialized.FindProperty("clearButton").objectReferenceValue = clear;
        serialized.FindProperty("status").objectReferenceValue = status;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildSettingsPanel(GameObject panel, MainMenuController controller)
    {
        GameObject content = NewChild("Audio Settings", panel.transform);
        Fill(content, 32f);
        VerticalLayoutGroup layout = AddVerticalLayout(content, 18f);
        layout.padding = new RectOffset(16, 16, 10, 10);

        Label(content.transform, "Heading", "SETTINGS", 34f, 54f);
        Slider music = SliderRow(content.transform, "Music", "MUSIC", out TMP_Text musicValue);
        Slider sfx = SliderRow(content.transform, "SFX", "SFX", out TMP_Text sfxValue);
        Button close = MakeButton(content.transform, "Close", "CLOSE", 190f);
        UnityEventTools.AddPersistentListener(close.onClick, controller.CloseSettingsPanel);

        AudioSettingsPanel wiring = content.AddComponent<AudioSettingsPanel>();
        SerializedObject serialized = new SerializedObject(wiring);
        serialized.FindProperty("musicSlider").objectReferenceValue = music;
        serialized.FindProperty("sfxSlider").objectReferenceValue = sfx;
        serialized.FindProperty("musicValue").objectReferenceValue = musicValue;
        serialized.FindProperty("sfxValue").objectReferenceValue = sfxValue;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Button BuildAccessButton(
        Transform parent,
        MainMenuController controller,
        Button settingsButton)
    {
        GameObject existing = Find(AccessButtonName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        GameObject host = NewChild(AccessButtonName, parent);
        RectTransform rect = (RectTransform)host.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-28f, 28f);
        rect.sizeDelta = new Vector2(260f, 70f);

        Image image = host.AddComponent<Image>();
        image.sprite = buttonSprite;
        image.type = buttonSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        Button button = host.AddComponent<Button>();
        button.targetGraphic = image;
        if (settingsButton != null)
        {
            button.colors = settingsButton.colors;
        }

        TMP_Text label = Label(host.transform, "Label", "ACCESS CODE", 21f, 70f);
        Fill(label.gameObject, 0f);
        label.color = new Color(0.12f, 0.16f, 0.2f, 1f);

        MainMenuHoverRelay hover = host.AddComponent<MainMenuHoverRelay>();
        SerializedObject hoverData = new SerializedObject(hover);
        hoverData.FindProperty("controller").objectReferenceValue = controller;
        hoverData.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(button.onClick, controller.ToggleAccessPanel);
        return button;
    }

    private static Slider SliderRow(
        Transform parent,
        string name,
        string caption,
        out TMP_Text valueLabel)
    {
        GameObject row = NewChild(name + " Row", parent);
        Size(row, new Vector2(0f, 64f));
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childForceExpandWidth = false;

        TMP_Text captionLabel = Label(row.transform, "Label", caption, 22f, 54f);
        Size(captionLabel.gameObject, new Vector2(120f, 54f));

        GameObject sliderHost = NewChild(name + " Slider", row.transform);
        Size(sliderHost, new Vector2(300f, 44f));
        Slider slider = sliderHost.AddComponent<Slider>();

        GameObject background = NewChild("Background", sliderHost.transform);
        RectTransform backgroundRect = (RectTransform)background.transform;
        backgroundRect.anchorMin = new Vector2(0f, 0.5f);
        backgroundRect.anchorMax = new Vector2(1f, 0.5f);
        backgroundRect.sizeDelta = new Vector2(0f, 12f);
        Image backgroundImage = background.AddComponent<Image>();
        backgroundImage.color = new Color(0.16f, 0.24f, 0.32f, 0.85f);

        GameObject fillArea = NewChild("Fill Area", sliderHost.transform);
        Fill(fillArea, 6f);
        GameObject fill = NewChild("Fill", fillArea.transform);
        Fill(fill, 0f);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.23f, 0.68f, 0.76f, 1f);

        GameObject handleArea = NewChild("Handle Slide Area", sliderHost.transform);
        Fill(handleArea, 10f);
        GameObject handle = NewChild("Handle", handleArea.transform);
        RectTransform handleRect = (RectTransform)handle.transform;
        handleRect.sizeDelta = new Vector2(26f, 26f);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = Color.white;

        slider.fillRect = (RectTransform)fill.transform;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;

        valueLabel = Label(row.transform, "Value", "100%", 20f, 54f);
        Size(valueLabel.gameObject, new Vector2(72f, 54f));
        return slider;
    }

    private static VerticalLayoutGroup AddVerticalLayout(GameObject host, float spacing)
    {
        VerticalLayoutGroup layout = host.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;
        return layout;
    }

    private static TMP_InputField Field(Transform parent)
    {
        GameObject host = NewChild("Code", parent);
        Size(host, new Vector2(0f, 54f));
        Image background = host.AddComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.94f);

        GameObject viewport = NewChild("Text Area", host.transform);
        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(14f, 8f);
        viewportRect.offsetMax = new Vector2(-14f, -8f);
        viewport.AddComponent<RectMask2D>();

        TMP_Text text = Label(viewport.transform, "Text", string.Empty, 22f, 38f);
        Fill(text.gameObject, 0f);
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        TMP_InputField field = host.AddComponent<TMP_InputField>();
        field.textViewport = viewportRect;
        field.textComponent = text;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.contentType = TMP_InputField.ContentType.Password;
        return field;
    }

    private static Button MakeButton(Transform parent, string name, string caption, float width)
    {
        GameObject host = NewChild(name, parent);
        Size(host, new Vector2(width, 52f));
        Image image = host.AddComponent<Image>();
        image.sprite = buttonSprite;
        image.type = buttonSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        Button button = host.AddComponent<Button>();
        button.targetGraphic = image;
        TMP_Text label = Label(host.transform, "Label", caption, 21f, 52f);
        Fill(label.gameObject, 0f);
        label.color = new Color(0.12f, 0.16f, 0.2f, 1f);
        return button;
    }

    private static void SetButtonCaption(Button button, string caption)
    {
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = caption;
            EditorUtility.SetDirty(label);
        }
    }

    private static TMP_Text Label(
        Transform parent,
        string name,
        string text,
        float fontSize,
        float height)
    {
        GameObject host = NewChild(name, parent);
        Size(host, new Vector2(0f, height));
        TextMeshProUGUI label = host.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.font = menuFont;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.color = new Color(0.12f, 0.16f, 0.2f, 1f);
        return label;
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
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void ClearChildren(Transform parent)
    {
        while (parent.childCount > 0)
        {
            Object.DestroyImmediate(parent.GetChild(0).gameObject);
        }
    }

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
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SessionProgressHud
{
    private readonly GameObject root;
    private readonly TMP_Text primaryCount;
    private readonly TMP_Text secondaryCount;
    private readonly TMP_Text caption;

    private SessionProgressHud(
        GameObject root,
        TMP_Text primaryCount,
        TMP_Text secondaryCount,
        TMP_Text caption)
    {
        this.root = root;
        this.primaryCount = primaryCount;
        this.secondaryCount = secondaryCount;
        this.caption = caption;
    }

    public static SessionProgressHud CreateConversation(
        Transform owner,
        Sprite studentHead,
        Sprite studentBody,
        TMP_FontAsset font)
    {
        RectTransform panel = CreatePanel(owner, "Student Progress", new Vector2(182f, 76f));
        RectTransform iconRoot = CreateRect(panel, "Student Icon", new Vector2(48f, 60f), new Vector2(36f, 0f));
        CreateImage(iconRoot, "Head", studentHead, new Vector2(25f, 25f), new Vector2(0f, 13f));
        CreateImage(iconRoot, "Body", studentBody, new Vector2(29f, 32f), new Vector2(0f, -14f));
        TMP_Text count = CreateText(panel, "Count", font, 29f, new Vector2(105f, 76f), new Vector2(35f, 0f));
        count.text = "0 / 0";
        return new SessionProgressHud(panel.gameObject, count, null, null);
    }

    public static SessionProgressHud CreateEvaluation(
        Transform owner,
        Sprite passedIcon,
        Sprite failedIcon,
        TMP_FontAsset font)
    {
        RectTransform panel = CreatePanel(owner, "Evaluation Progress", new Vector2(238f, 92f));
        CreateImage(panel, "Passed Icon", passedIcon, new Vector2(44f, 36f), new Vector2(-74f, 10f));
        CreateImage(panel, "Failed Icon", failedIcon, new Vector2(39f, 39f), new Vector2(23f, 10f));
        TMP_Text passed = CreateText(panel, "Passed Count", font, 25f, new Vector2(48f, 45f), new Vector2(-34f, 10f));
        TMP_Text failed = CreateText(panel, "Failed Count", font, 25f, new Vector2(48f, 45f), new Vector2(62f, 10f));
        TMP_Text caption = CreateText(panel, "Evaluated Count", font, 16f, new Vector2(210f, 28f), new Vector2(0f, -29f));
        return new SessionProgressHud(panel.gameObject, passed, failed, caption);
    }

    public void SetConversationCounts(int completed, int total)
    {
        if (primaryCount != null)
        {
            primaryCount.text = completed + " / " + total;
        }
    }

    public void SetEvaluationCounts(int passed, int failed, int evaluated, int total)
    {
        if (primaryCount != null)
        {
            primaryCount.text = passed.ToString();
        }

        if (secondaryCount != null)
        {
            secondaryCount.text = failed.ToString();
        }

        if (caption != null)
        {
            caption.text = evaluated + " / " + total + " assessed";
        }
    }

    public void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.SetActive(visible);
        }
    }

    private static RectTransform CreatePanel(Transform owner, string name, Vector2 size)
    {
        GameObject canvasObject = new GameObject(name + " Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(owner, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreateRect(canvasObject.transform, name, size, new Vector2(18f, -18f));
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);

        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(1f, 0.955f, 0.85f, 0.96f);
        background.raycastTarget = false;
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.31f, 0.22f, 0.17f, 0.82f);
        outline.effectDistance = new Vector2(3f, -3f);
        return panel;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size, Vector2 position)
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

    private static void CreateImage(
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
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.22f, 0.12f, 0.09f, 1f);
        text.raycastTarget = false;
        return text;
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SessionProgressHud
{
    private readonly GameObject root;
    private readonly TMP_Text primaryCount;

    private SessionProgressHud(
        GameObject root,
        TMP_Text primaryCount)
    {
        this.root = root;
        this.primaryCount = primaryCount;
    }

    public static SessionProgressHud CreateConversation(
        Transform owner,
        Sprite studentHead,
        Sprite studentBody,
        TMP_FontAsset font)
    {
        RectTransform panel = CreatePanel(owner, "Student Progress", new Vector2(188f, 72f));
        RectTransform iconRoot = CreateRect(panel, "Student Icon", new Vector2(48f, 60f), new Vector2(-58f, 0f));
        if (studentBody == null)
        {
            CreateImage(iconRoot, "Student", studentHead, new Vector2(29f, 52f), Vector2.zero);
        }
        else
        {
            CreateImage(iconRoot, "Head", studentHead, new Vector2(25f, 25f), new Vector2(0f, 13f));
            CreateImage(iconRoot, "Body", studentBody, new Vector2(29f, 32f), new Vector2(0f, -14f));
        }
        TMP_Text count = CreateText(panel, "Count", font, 29f, new Vector2(116f, 72f), new Vector2(28f, 0f));
        count.alignment = TextAlignmentOptions.MidlineLeft;
        count.text = "0 / 0";
        return new SessionProgressHud(panel.gameObject, count);
    }

    public void SetConversationCounts(int completed, int total)
    {
        if (primaryCount != null)
        {
            primaryCount.text = completed + " / " + total;
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
        Transform canvasRoot = FindMainCanvas(owner);
        RectTransform panel = CreateRect(canvasRoot != null ? canvasRoot : owner, name, size, new Vector2(18f, -18f));
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.SetAsLastSibling();

        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(1f, 0.955f, 0.85f, 0.96f);
        background.raycastTarget = false;
        return panel;
    }

    private static Transform FindMainCanvas(Transform owner)
    {
#if UNITY_2023_1_OR_NEWER
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);
#endif
        for (int index = 0; index < canvases.Length; index++)
        {
            Canvas canvas = canvases[index];
            if (canvas != null && canvas.name == "Main Canvas")
            {
                return canvas.transform;
            }
        }

        return owner != null ? owner.GetComponentInParent<Canvas>()?.transform : null;
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

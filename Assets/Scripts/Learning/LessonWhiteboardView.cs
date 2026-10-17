using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whiteboard in the Learning phase: a heading and a few bullets, as text.
///
/// It always shows the whole board for a step, never a delta, so Back and a
/// replay need nothing but the step they land on: call <see cref="Show"/> with
/// that step's board and the old one is gone. Figures from the Source Material
/// are not drawn yet; <c>figureId</c> is reserved on the wire and ignored here.
///
/// Built from code, with no scene dependency, so the scene builder can add it
/// with <see cref="Create"/> and a scene that already holds one can reference
/// its texts instead. The default look is a green board with chalk-white text
/// across the top centre of the screen. Move <see cref="Rect"/> to place it.
/// </summary>
[DisallowMultipleComponent]
public sealed class LessonWhiteboardView : MonoBehaviour
{
    private const string BulletPrefix = "• ";
    private static readonly Color BoardColor = new Color(0.16f, 0.36f, 0.27f, 0.97f);
    private static readonly Color ChalkColor = new Color(0.96f, 0.97f, 0.92f, 1f);

    [SerializeField] private TMP_Text headingText;
    [SerializeField] private TMP_Text bulletsText;

    private RectTransform rect;

    public RectTransform Rect => rect != null ? rect : (rect = transform as RectTransform);

    /// <summary>The heading as shown. Empty when the board is clear.</summary>
    public string Heading => headingText != null ? headingText.text : string.Empty;

    /// <summary>The bullets as shown, one per line with a leading dot.</summary>
    public string Bullets => bulletsText != null ? bulletsText.text : string.Empty;

    /// <summary>
    /// Build a whiteboard under <paramref name="parent"/>. The font is optional:
    /// without one the project's default TextMeshPro font is used.
    /// </summary>
    public static LessonWhiteboardView Create(Transform parent, TMP_FontAsset font = null)
    {
        GameObject boardObject = new GameObject(
            "Lesson Whiteboard",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        boardObject.layer = 5;
        RectTransform boardRect = boardObject.GetComponent<RectTransform>();
        boardRect.SetParent(parent, false);

        // Awake runs as the component is added and builds the texts with the
        // default font, so a font passed in is applied afterwards.
        LessonWhiteboardView view = boardObject.AddComponent<LessonWhiteboardView>();
        view.SetFont(font);
        return view;
    }

    /// <summary>Use this font for the heading and the bullets. Null leaves them as they are.</summary>
    public void SetFont(TMP_FontAsset font)
    {
        Build();
        if (font == null)
        {
            return;
        }

        headingText.font = font;
        bulletsText.font = font;
    }

    private void Awake()
    {
        // Safe when the board was authored in a scene: it only builds what is
        // missing, and Create has already built everything by the time this
        // runs on a code-made one.
        Build();
    }

    /// <summary>
    /// Show a step's board. A null board, or one with nothing on it, clears
    /// the text and leaves the board itself up.
    /// </summary>
    public void Show(FleeWhiteboard board)
    {
        Build();

        if (board == null)
        {
            Clear();
            return;
        }

        headingText.text = string.IsNullOrWhiteSpace(board.heading) ? string.Empty : board.heading.Trim();
        bulletsText.text = FormatBullets(board.bullets);
    }

    public void Clear()
    {
        Build();
        headingText.text = string.Empty;
        bulletsText.text = string.Empty;
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    /// <summary>
    /// One bullet per line, each with a leading dot. Blank and null entries are
    /// left out so a stray empty string does not draw an empty dot.
    /// </summary>
    public static string FormatBullets(string[] bullets)
    {
        if (bullets == null || bullets.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        for (int index = 0; index < bullets.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(bullets[index]))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(BulletPrefix).Append(bullets[index].Trim());
        }

        return builder.ToString();
    }

    private void Build()
    {
        if (headingText != null && bulletsText != null)
        {
            return;
        }

        RectTransform root = Rect;
        if (root == null)
        {
            return;
        }

        if (headingText == null && bulletsText == null)
        {
            // Placed only when this component made the board. A board that was
            // authored in a scene keeps the layout it was given.
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -28f);
            root.sizeDelta = new Vector2(820f, 300f);

            Image background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
            }

            background.color = BoardColor;
            background.raycastTarget = false;
        }

        TMP_FontAsset resolvedFont = TMP_Settings.defaultFontAsset;
        if (headingText == null)
        {
            headingText = CreateText(
                "Heading",
                resolvedFont,
                new Vector2(0f, 0.74f),
                new Vector2(1f, 1f),
                new Vector2(36f, 0f),
                new Vector2(-36f, -14f),
                44f,
                28f,
                FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft);
        }

        if (bulletsText == null)
        {
            bulletsText = CreateText(
                "Bullets",
                resolvedFont,
                Vector2.zero,
                new Vector2(1f, 0.74f),
                new Vector2(36f, 18f),
                new Vector2(-36f, 0f),
                34f,
                20f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
        }
    }

    private TMP_Text CreateText(
        string objectName,
        TMP_FontAsset font,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        float fontSizeMax,
        float fontSizeMin,
        FontStyles style,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = 5;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(transform, false);
        textRect.anchorMin = anchorMin;
        textRect.anchorMax = anchorMax;
        textRect.offsetMin = offsetMin;
        textRect.offsetMax = offsetMax;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontStyle = style;
        text.fontSize = fontSizeMax;
        text.enableAutoSizing = true;
        text.fontSizeMin = fontSizeMin;
        text.fontSizeMax = fontSizeMax;
        text.alignment = alignment;
        text.color = ChalkColor;
        text.raycastTarget = false;
        text.text = string.Empty;
        return text;
    }
}

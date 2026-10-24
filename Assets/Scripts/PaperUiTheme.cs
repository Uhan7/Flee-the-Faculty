using System;
using UnityEngine;

[CreateAssetMenu(fileName = "Paper UI Theme", menuName = "Flee the Faculty/Paper UI Theme")]
public sealed class PaperUiTheme : ScriptableObject
{
    private const string ResourcePath = "Paper UI Theme";

    private static readonly string[] StyleNames =
    {
        "Aqua",
        "Blue",
        "Purple",
        "Violet",
        "Pink",
        "Green",
        "Mint",
        "Yellow",
        "Orange",
        "Red"
    };

    private static readonly Color[] AccentColors =
    {
        new Color(0.02f, 0.58f, 0.64f, 1f),
        new Color(0.08f, 0.47f, 0.78f, 1f),
        new Color(0.39f, 0.14f, 0.83f, 1f),
        new Color(0.63f, 0.12f, 0.68f, 1f),
        new Color(0.83f, 0.09f, 0.36f, 1f),
        new Color(0.12f, 0.58f, 0.24f, 1f),
        new Color(0.02f, 0.57f, 0.42f, 1f),
        new Color(0.92f, 0.58f, 0.03f, 1f),
        new Color(0.95f, 0.34f, 0.02f, 1f),
        new Color(0.91f, 0.12f, 0.14f, 1f)
    };

    [SerializeField] private Sprite neutralPaper;
    [SerializeField] private Sprite[] paperColors = Array.Empty<Sprite>();
    [SerializeField] private Sprite[] roundedPapers = Array.Empty<Sprite>();
    [SerializeField] private Sprite ruledLine;
    [SerializeField] private Sprite tape;
    [SerializeField] private Sprite paperClip;
    [SerializeField] private Sprite thumbtack;
    [SerializeField] private Sprite robotPanel;
    [SerializeField] private Sprite robotSection;
    [SerializeField] private Sprite robotButton;

    public Sprite NeutralPaper => neutralPaper;
    public Sprite RuledLine => ruledLine;
    public Sprite Tape => tape;
    public Sprite PaperClip => paperClip;
    public Sprite Thumbtack => thumbtack;
    public Sprite RobotPanel => robotPanel;
    public Sprite RobotSection => robotSection;
    public Sprite RobotButton => robotButton;
    public int StyleCount => Mathf.Min(StyleNames.Length, paperColors != null ? paperColors.Length : 0);

    public static PaperUiTheme Load()
    {
        return Resources.Load<PaperUiTheme>(ResourcePath);
    }

    public Sprite GetPaper(int styleIndex)
    {
        return GetSprite(paperColors, styleIndex, neutralPaper);
    }

    public Sprite GetRoundedPaper(int styleIndex)
    {
        return GetSprite(roundedPapers, styleIndex, GetPaper(styleIndex));
    }

    public string GetStyleName(int styleIndex)
    {
        return StyleNames[WrapIndex(styleIndex, StyleNames.Length)];
    }

    public Color GetAccent(int styleIndex)
    {
        return AccentColors[WrapIndex(styleIndex, AccentColors.Length)];
    }

    public int FindClosestStyle(Color color)
    {
        Color.RGBToHSV(color, out float targetHue, out float targetSaturation, out _);
        int bestIndex = 0;
        float bestDistance = float.MaxValue;
        for (int index = 0; index < AccentColors.Length; index++)
        {
            Color accent = AccentColors[index];
            Color.RGBToHSV(accent, out float accentHue, out float accentSaturation, out _);
            float hueDifference = Mathf.Abs(targetHue - accentHue);
            hueDifference = Mathf.Min(hueDifference, 1f - hueDifference);
            float saturationDifference = targetSaturation - accentSaturation;
            float distance = (hueDifference * hueDifference) +
                (saturationDifference * saturationDifference * 0.04f);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

#if UNITY_EDITOR
    public void Configure(
        Sprite neutral,
        Sprite[] coloredPapers,
        Sprite[] coloredRoundedPapers,
        Sprite line,
        Sprite tapeSprite,
        Sprite clipSprite,
        Sprite tackSprite,
        Sprite robotPanelSprite,
        Sprite robotSectionSprite,
        Sprite robotButtonSprite)
    {
        neutralPaper = neutral;
        paperColors = coloredPapers ?? Array.Empty<Sprite>();
        roundedPapers = coloredRoundedPapers ?? Array.Empty<Sprite>();
        ruledLine = line;
        tape = tapeSprite;
        paperClip = clipSprite;
        thumbtack = tackSprite;
        robotPanel = robotPanelSprite;
        robotSection = robotSectionSprite;
        robotButton = robotButtonSprite;
    }
#endif

    private static Sprite GetSprite(Sprite[] sprites, int index, Sprite fallback)
    {
        if (sprites == null || sprites.Length == 0)
        {
            return fallback;
        }

        Sprite result = sprites[WrapIndex(index, sprites.Length)];
        return result != null ? result : fallback;
    }

    private static int WrapIndex(int index, int count)
    {
        return count > 0 ? (index % count + count) % count : 0;
    }
}

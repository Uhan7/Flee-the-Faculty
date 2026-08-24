using UnityEngine;

public interface IDialogueView
{
    bool IsRevealComplete { get; }
    void SetVisible(bool visible);
    void DisplayLine(IDialogueLine line, string visibleText, bool canAdvance);
    void CompleteReveal();
}

[DisallowMultipleComponent]
public sealed class DialogueView : MonoBehaviour, IDialogueView
{
    [SerializeField, Range(0.5f, 0.95f)] private float screenWidth = 0.82f;
    [SerializeField, Min(100f)] private float panelHeight = 160f;

    private bool isVisible;
    private bool canAdvance;
    private string speaker = string.Empty;
    private string body = string.Empty;
    private GUIStyle panelStyle;
    private GUIStyle speakerStyle;
    private GUIStyle bodyStyle;
    private GUIStyle continueStyle;
    private Texture2D panelTexture;

    public bool IsRevealComplete => true;

    public void SetVisible(bool visible)
    {
        isVisible = visible;
    }

    public void DisplayLine(IDialogueLine line, string visibleText, bool lineCanAdvance)
    {
        speaker = line != null ? line.SpeakerName : string.Empty;
        body = visibleText ?? string.Empty;
        canAdvance = lineCanAdvance;
    }

    public void CompleteReveal()
    {
    }

    private void OnGUI()
    {
        if (!isVisible)
        {
            return;
        }

        EnsureStyles();

        Rect safeArea = Screen.safeArea;
        float width = safeArea.width * screenWidth;
        float height = Mathf.Min(panelHeight, safeArea.height * 0.32f);
        Rect panelRect = new Rect(
            safeArea.x + (safeArea.width - width) * 0.5f,
            safeArea.yMax - height - 24f,
            width,
            height);

        GUI.Box(panelRect, GUIContent.none, panelStyle);
        GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 16f, panelRect.width - 48f, 30f), speaker, speakerStyle);
        GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 49f, panelRect.width - 48f, panelRect.height - 78f), body, bodyStyle);

        if (canAdvance)
        {
            GUI.Label(
                new Rect(panelRect.x + 24f, panelRect.yMax - 29f, panelRect.width - 48f, 20f),
                "Click or press Space to continue",
                continueStyle);
        }
    }

    private void OnDestroy()
    {
        if (panelTexture != null)
        {
            Destroy(panelTexture);
        }
    }

    private void EnsureStyles()
    {
        if (panelStyle != null)
        {
            return;
        }

        panelTexture = new Texture2D(1, 1)
        {
            name = "Temporary Dialogue Background",
            hideFlags = HideFlags.HideAndDontSave
        };
        panelTexture.SetPixel(0, 0, new Color(0.035f, 0.055f, 0.06f, 0.94f));
        panelTexture.Apply();

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = panelTexture;

        speakerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold
        };
        speakerStyle.normal.textColor = new Color(0.96f, 0.78f, 0.28f);

        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        bodyStyle.normal.textColor = new Color(0.95f, 0.97f, 0.94f);

        continueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleRight
        };
        continueStyle.normal.textColor = new Color(0.65f, 0.78f, 0.75f);
    }
}

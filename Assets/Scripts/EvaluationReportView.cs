using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EvaluationReportView : MonoBehaviour
{
    private static readonly Color BackdropColor = new Color(0.08f, 0.06f, 0.04f, 0.72f);
    private static readonly Color PaperColor = new Color(0.96f, 0.9f, 0.74f, 1f);
    private static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f, 1f);
    private static readonly Color AccentColor = new Color(0.49f, 0.25f, 0.12f, 1f);
    private const int ReportPaperStyle = 7;

    public static void Show(
        FleeClassroomSession classroom,
        IReadOnlyList<FleePupilSession> pupils,
        FleeTeacherSceneResult teacherResult,
        string title,
        string savedLabel,
        string accuracyLabel,
        string teacherNoteLabel,
        string returnLabel,
        System.Action onReturn)
    {
        GameObject host = new GameObject("AraBOT Evaluation Report", typeof(RectTransform));
        EvaluationReportView view = host.AddComponent<EvaluationReportView>();
        view.Build(
            classroom,
            pupils,
            teacherResult,
            title,
            savedLabel,
            accuracyLabel,
            teacherNoteLabel,
            returnLabel,
            onReturn);
    }

    private void Build(
        FleeClassroomSession classroom,
        IReadOnlyList<FleePupilSession> pupils,
        FleeTeacherSceneResult teacherResult,
        string title,
        string savedLabel,
        string accuracyLabel,
        string teacherNoteLabel,
        string returnLabel,
        System.Action onReturn)
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = transform as RectTransform;
        Stretch(root);

        Image blocker = CreateImage("Backdrop", root, BackdropColor);
        Stretch(blocker.rectTransform);

        PaperUiTheme paperTheme = PaperUiTheme.Load();
        Image paper = CreateImage("Paper", root, PaperColor);
        if (paperTheme != null && paperTheme.GetPaper(ReportPaperStyle) != null)
        {
            paper.sprite = paperTheme.GetPaper(ReportPaperStyle);
            paper.type = Image.Type.Sliced;
            paper.color = Color.white;
        }

        RectTransform paperRect = paper.rectTransform;
        paperRect.anchorMin = new Vector2(0.5f, 0.5f);
        paperRect.anchorMax = new Vector2(0.5f, 0.5f);
        paperRect.pivot = new Vector2(0.5f, 0.5f);
        paperRect.sizeDelta = new Vector2(920f, 900f);
        paperRect.localRotation = Quaternion.Euler(0f, 0f, -0.7f);
        Shadow shadow = paper.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
        shadow.effectDistance = new Vector2(14f, -16f);
        if (paperTheme == null)
        {
            Outline outline = paper.gameObject.AddComponent<Outline>();
            outline.effectColor = AccentColor;
            outline.effectDistance = new Vector2(3f, -3f);
        }

        CreateRuledLines(paperRect, paperTheme, ReportPaperStyle);

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        TMP_Text titleText = CreateText("Report Title", paperRect, font, title, 46f, FontStyles.Bold);
        SetRect(titleText.rectTransform, new Vector2(58f, -48f), new Vector2(-58f, -118f));
        titleText.alignment = TextAlignmentOptions.Center;

        int total = pupils != null ? pupils.Count : 0;
        int saved = 0;
        int scoreTotal = 0;
        for (int index = 0; index < total; index++)
        {
            FleePupilSession pupil = pupils[index];
            bool rescued = pupil != null && pupil.Satisfied && !string.IsNullOrWhiteSpace(pupil.LearnedAnswer);
            if (rescued)
            {
                saved++;
            }

            scoreTotal += pupil != null ? Mathf.Clamp(pupil.LastScore, 0, 100) : 0;
        }

        int averageScore = total > 0 ? Mathf.RoundToInt((float)scoreTotal / total) : 0;
        string topic = classroom != null && !string.IsNullOrWhiteSpace(classroom.Topic)
            ? ResolveDisplayTopic(classroom.Topic)
            : "Today's lesson";
        int quota = classroom != null ? classroom.RescueQuota : teacherResult != null ? teacherResult.RescueQuota : 0;
        bool cleared = saved >= quota;

        TMP_Text topicText = CreateText(
            "Topic",
            paperRect,
            font,
            "<b>Topic:</b> " + topic,
            25f,
            FontStyles.Normal);
        SetRect(topicText.rectTransform, new Vector2(72f, -132f), new Vector2(-72f, -178f));

        Image summaryCard = CreateImage("Summary", paperRect, new Color(1f, 0.98f, 0.88f, 0.84f));
        ApplyPaperSprite(
            summaryCard,
            paperTheme != null ? paperTheme.GetRoundedPaper(cleared ? 5 : 9) : null,
            0.96f);
        AddPaperShadow(summaryCard, new Vector2(5f, -6f), 0.18f);
        SetRect(summaryCard.rectTransform, new Vector2(64f, -188f), new Vector2(-64f, -330f));
        TMP_Text summaryText = CreateText(
            "Summary Text",
            summaryCard.rectTransform,
            font,
            (cleared ? "CLASS CLEARED!" : "KEEP PRACTISING") + "\n" +
            savedLabel + ":  <b>" + saved + " / " + total + "</b>   •   Needed: " + quota + "\n" +
            accuracyLabel + ":  <b>" + averageScore + "%</b>",
            27f,
            FontStyles.Normal);
        Stretch(summaryText.rectTransform, 24f, 18f, 24f, 18f);
        summaryText.alignment = TextAlignmentOptions.Center;
        summaryText.color = cleared ? new Color(0.12f, 0.36f, 0.17f, 1f) : AccentColor;

        ScrollRect scroll = CreateScrollView(paperRect, font, paperTheme);
        SetRect(scroll.GetComponent<RectTransform>(), new Vector2(68f, -346f), new Vector2(-68f, -746f));
        TMP_Text detailsText = scroll.content.GetComponent<TMP_Text>();
        detailsText.text = BuildDetails(pupils, teacherResult, teacherNoteLabel);
        detailsText.ForceMeshUpdate();
        scroll.content.sizeDelta = new Vector2(
            scroll.content.sizeDelta.x,
            Mathf.Max(400f, detailsText.preferredHeight + 30f));

        Button returnButton = CreateButton("Return Button", paperRect, font, returnLabel, paperTheme);
        RectTransform buttonRect = returnButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 54f);
        buttonRect.sizeDelta = new Vector2(470f, 92f);
        returnButton.onClick.AddListener(() =>
        {
            returnButton.interactable = false;
            onReturn?.Invoke();
        });
    }

    private static string ResolveDisplayTopic(string topic)
    {
        string safeTopic = topic != null ? topic.Trim() : string.Empty;
        const string generatedLessonMarker = ". Create every student misconception";
        int markerIndex = safeTopic.IndexOf(generatedLessonMarker, System.StringComparison.Ordinal);
        if (markerIndex > 0)
        {
            safeTopic = safeTopic.Substring(0, markerIndex);
        }

        const int maximumCharacters = 90;
        return safeTopic.Length > maximumCharacters
            ? safeTopic.Substring(0, maximumCharacters).TrimEnd() + "…"
            : safeTopic;
    }

    private static string BuildDetails(
        IReadOnlyList<FleePupilSession> pupils,
        FleeTeacherSceneResult teacherResult,
        string teacherNoteLabel)
    {
        StringBuilder builder = new StringBuilder();
        if (pupils != null)
        {
            for (int index = 0; index < pupils.Count; index++)
            {
                FleePupilSession pupil = pupils[index];
                if (pupil == null)
                {
                    continue;
                }

                bool rescued = pupil.Satisfied && !string.IsNullOrWhiteSpace(pupil.LearnedAnswer);
                string statusColor = rescued ? "#2F7D46" : "#B45632";
                builder.Append("<color=").Append(statusColor).Append(">●</color>  ");
                builder.Append("<b>").Append(pupil.Name).Append("</b>    ");
                builder.Append("<color=").Append(statusColor).Append("><b>");
                builder.Append(rescued ? "SAVED" : "NEEDS HELP");
                builder.Append("</b></color>   •   ");
                builder.Append(Mathf.Clamp(pupil.LastScore, 0, 100)).Append("%\n");
                if (!string.IsNullOrWhiteSpace(pupil.LearnedAnswer))
                {
                    builder.Append("    <b>Learned:</b> ").Append(pupil.LearnedAnswer.Trim()).Append("\n");
                }

                if (!rescued)
                {
                    builder.Append("    <b>Why:</b> ")
                        .Append(BuildNeedsHelpReason(pupil, teacherResult))
                        .Append("\n");
                }

                if (index < pupils.Count - 1)
                {
                    builder.Append("<color=#B9AA83>────────────────────────────────────</color>\n");
                }

                builder.Append('\n');
            }
        }

        if (teacherResult != null && !string.IsNullOrWhiteSpace(teacherResult.TeacherRemark))
        {
            builder.Append("<color=#A58E62>────────────────────────────────────</color>\n");
            builder.Append("<b>").Append(teacherNoteLabel).Append(":</b>\n");
            builder.Append(teacherResult.TeacherRemark.Trim());
        }

        return builder.ToString();
    }

    private static string BuildNeedsHelpReason(
        FleePupilSession pupil,
        FleeTeacherSceneResult teacherResult)
    {
        FleeTeacherPupilResult result = FindTeacherResult(pupil, teacherResult);
        string misconception = result != null && !string.IsNullOrWhiteSpace(result.Misconception)
            ? result.Misconception.Trim()
            : pupil.Misconception?.Trim();
        if (!string.IsNullOrWhiteSpace(misconception))
        {
            return "Still believed: \"" + TrimReportText(misconception, 150) + "\"";
        }

        if (string.IsNullOrWhiteSpace(pupil.LearnedAnswer))
        {
            return pupil.TurnsUsed > 0
                ? "No correct final explanation was recorded after the available tries."
                : "No final explanation was recorded yet.";
        }

        return "The final explanation did not fully meet the lesson goal yet.";
    }

    private static FleeTeacherPupilResult FindTeacherResult(
        FleePupilSession pupil,
        FleeTeacherSceneResult teacherResult)
    {
        if (pupil == null || teacherResult?.Results == null)
        {
            return null;
        }

        for (int index = 0; index < teacherResult.Results.Length; index++)
        {
            FleeTeacherPupilResult result = teacherResult.Results[index];
            if (result == null)
            {
                continue;
            }

            bool idMatches = !string.IsNullOrWhiteSpace(pupil.PupilId)
                && string.Equals(
                    pupil.PupilId,
                    result.PupilId,
                    System.StringComparison.Ordinal);
            bool nameMatches = !string.IsNullOrWhiteSpace(pupil.Name)
                && string.Equals(
                    pupil.Name,
                    result.Name,
                    System.StringComparison.OrdinalIgnoreCase);
            if (idMatches || nameMatches)
            {
                return result;
            }
        }

        return null;
    }

    private static string TrimReportText(string value, int maximumCharacters)
    {
        string trimmed = value != null ? value.Trim() : string.Empty;
        return trimmed.Length > maximumCharacters
            ? trimmed.Substring(0, maximumCharacters).TrimEnd() + "…"
            : trimmed;
    }

    private static ScrollRect CreateScrollView(
        RectTransform parent,
        TMP_FontAsset font,
        PaperUiTheme paperTheme)
    {
        Image viewportImage = CreateImage("Details Viewport", parent, new Color(1f, 1f, 1f, 0.22f));
        ApplyPaperSprite(
            viewportImage,
            paperTheme != null ? paperTheme.GetRoundedPaper(1) : null,
            0.72f);
        AddPaperShadow(viewportImage, new Vector2(5f, -6f), 0.16f);
        RectTransform viewportRect = viewportImage.rectTransform;
        viewportImage.gameObject.AddComponent<RectMask2D>();

        TMP_Text details = CreateText(
            "Details",
            viewportRect,
            font,
            string.Empty,
            18f,
            FontStyles.Normal);
        RectTransform detailsRect = details.rectTransform;
        detailsRect.anchorMin = new Vector2(0f, 1f);
        detailsRect.anchorMax = new Vector2(1f, 1f);
        detailsRect.pivot = new Vector2(0.5f, 1f);
        detailsRect.anchoredPosition = new Vector2(0f, -20f);
        detailsRect.sizeDelta = new Vector2(-80f, 380f);
        details.lineSpacing = 1f;

        ScrollRect scroll = viewportImage.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = detailsRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        return scroll;
    }

    private static Button CreateButton(
        string name,
        RectTransform parent,
        TMP_FontAsset font,
        string label,
        PaperUiTheme paperTheme)
    {
        Image image = CreateImage(name, parent, AccentColor);
        ApplyPaperSprite(
            image,
            paperTheme != null ? paperTheme.GetPaper(1) : null,
            1f);
        AddPaperShadow(image, new Vector2(5f, -6f), 0.2f);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        TMP_Text text = CreateText("Label", image.rectTransform, font, label, 27f, FontStyles.Bold);
        Stretch(text.rectTransform, 18f, 10f, 18f, 10f);
        text.alignment = TextAlignmentOptions.Center;
        text.color = paperTheme != null ? InkColor : PaperColor;
        return button;
    }

    private static void CreateRuledLines(
        RectTransform paperRect,
        PaperUiTheme paperTheme,
        int styleIndex)
    {
        if (paperTheme == null || paperTheme.RuledLine == null)
        {
            return;
        }

        Color accent = paperTheme.GetAccent(styleIndex);
        for (int index = 0; index < 20; index++)
        {
            Image line = CreateImage(
                "Ruled Line " + (index + 1),
                paperRect,
                new Color(accent.r, accent.g, accent.b, 0.14f));
            line.sprite = paperTheme.RuledLine;
            line.type = Image.Type.Simple;
            line.raycastTarget = false;
            RectTransform lineRect = line.rectTransform;
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0.5f, 1f);
            lineRect.anchoredPosition = new Vector2(0f, -150f - (index * 34f));
            lineRect.sizeDelta = new Vector2(-96f, 3f);
        }
    }

    private static void ApplyPaperSprite(Image image, Sprite sprite, float alpha)
    {
        if (image == null || sprite == null)
        {
            return;
        }

        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.preserveAspect = false;
        image.color = new Color(1f, 1f, 1f, alpha);
    }

    private static void AddPaperShadow(Image image, Vector2 distance, float alpha)
    {
        if (image == null || image.GetComponent<Shadow>() != null)
        {
            return;
        }

        Shadow shadow = image.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.02f, 0.015f, 0.01f, alpha);
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.transform.SetParent(parent, false);
        Image image = child.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        TMP_FontAsset font,
        string value,
        float size,
        FontStyles style)
    {
        GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        child.transform.SetParent(parent, false);
        TMP_Text text = child.GetComponent<TMP_Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = InkColor;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(offsetMin.x, offsetMax.y);
        rect.offsetMax = new Vector2(offsetMax.x, offsetMin.y);
    }

    private static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}

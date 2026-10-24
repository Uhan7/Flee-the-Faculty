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
    [SerializeField, Min(0f)] private float maximumTiltAngle = 5f;

    public static void Show(
        FleeClassroomSession classroom,
        IReadOnlyList<FleePupilSession> pupils,
        FleeTeacherSceneResult teacherResult,
        string title,
        string savedLabel,
        string accuracyLabel,
        string teacherNoteLabel,
        string returnLabel,
        System.Action onReturn,
        System.Action onRetry = null,
        float maximumTiltAngle = 5f)
    {
        GameObject host = new GameObject("AraBOT Evaluation Report", typeof(RectTransform));
        EvaluationReportView view = host.AddComponent<EvaluationReportView>();
        view.maximumTiltAngle = Mathf.Max(0f, maximumTiltAngle);
        view.Build(
            classroom,
            pupils,
            teacherResult,
            title,
            savedLabel,
            accuracyLabel,
            teacherNoteLabel,
            returnLabel,
            onReturn,
            onRetry);
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
        System.Action onReturn,
        System.Action onRetry)
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
        int paperStyle = classroom != null ? classroom.PaperStyle : PlayerPrefs.GetInt("Flee.ClassroomPaperStyle", 1);
        Color paperAccent = paperTheme != null ? paperTheme.GetAccent(paperStyle) : AccentColor;
        Image paper = CreateImage("Paper", root, PaperColor);
        if (paperTheme != null && paperTheme.GetPaper(paperStyle) != null)
        {
            paper.sprite = paperTheme.GetPaper(paperStyle);
            paper.type = Image.Type.Sliced;
            paper.color = Color.white;
        }

        RectTransform paperRect = paper.rectTransform;
        paperRect.anchorMin = new Vector2(0.5f, 0.5f);
        paperRect.anchorMax = new Vector2(0.5f, 0.5f);
        paperRect.pivot = new Vector2(0.5f, 0.5f);
        paperRect.sizeDelta = new Vector2(920f, 850f);
        paperRect.anchoredPosition = new Vector2(0f, 60f);
        paperRect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-maximumTiltAngle, maximumTiltAngle));
        Shadow shadow = paper.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
        shadow.effectDistance = new Vector2(14f, -16f);
        if (paperTheme == null)
        {
            Outline outline = paper.gameObject.AddComponent<Outline>();
            outline.effectColor = AccentColor;
            outline.effectDistance = new Vector2(3f, -3f);
        }

        CreateRuledLines(paperRect, paperTheme, paperStyle);

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        TMP_Text titleText = CreateText("Report Title", paperRect, font, title, 38f, FontStyles.Bold);
        SetRect(titleText.rectTransform, new Vector2(64f, -48f), new Vector2(-210f, -116f));
        titleText.alignment = TextAlignmentOptions.Left;

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
        SetRect(topicText.rectTransform, new Vector2(72f, -126f), new Vector2(-210f, -170f));
        topicText.enableAutoSizing = true;
        topicText.fontSizeMin = 18f;
        topicText.fontSizeMax = 25f;
        CreateDivider(paperRect, "Topic Divider", -174f, paperAccent);

        TMP_Text classInfo = CreateText("Class Information", paperRect, font,
            "Students: " + total + "    •    Expected grade level: " + (classroom != null ? classroom.GradeLevel.ToString() : "—"),
            22f, FontStyles.Normal);
        SetRect(classInfo.rectTransform, new Vector2(72f, -180f), new Vector2(-72f, -214f));
        CreateDivider(paperRect, "Heading Divider", -224f, paperAccent);
        CreateRatingBadge(paperRect, font, averageScore);

        Image summaryCard = CreateImage("Summary", paperRect, new Color(1f, 0.98f, 0.88f, 0.84f));
        ApplyPaperSprite(
            summaryCard,
            paperTheme != null ? paperTheme.GetRoundedPaper(cleared ? 5 : 9) : null,
            0.96f);
        AddPaperShadow(summaryCard, new Vector2(5f, -6f), 0.18f);
        SetRect(summaryCard.rectTransform, new Vector2(64f, -240f), new Vector2(-64f, -356f));
        TMP_Text summaryText = CreateText(
            "Summary Text",
            summaryCard.rectTransform,
            font,
            (cleared ? "CLASS CLEARED!" : "KEEP PRACTISING") + "\n" +
            savedLabel + ":  <b>" + saved + " / " + total + "</b>   •   Needed: " + quota + "\n" +
            accuracyLabel + ":  <b>" + averageScore + "%</b>",
            24f,
            FontStyles.Normal);
        Stretch(summaryText.rectTransform, 24f, 18f, 24f, 18f);
        summaryText.alignment = TextAlignmentOptions.Center;
        summaryText.color = cleared ? new Color(0.12f, 0.36f, 0.17f, 1f) : AccentColor;

        ScrollRect scroll = CreateScrollView(paperRect, font, paperTheme);
        SetRect(scroll.GetComponent<RectTransform>(), new Vector2(68f, -376f), new Vector2(-68f, -806f));
        BuildDetailRows(scroll.content, pupils, teacherResult, teacherNoteLabel, font, paperAccent);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.verticalNormalizedPosition = 1f;
        TMP_Text scrollHint = CreateText("Scroll Hint", paperRect, font,
            "Scroll for more students and teacher feedback", 14f, FontStyles.Normal);
        SetRect(scrollHint.rectTransform, new Vector2(72f, -358f), new Vector2(-72f, -374f));
        scrollHint.alignment = TextAlignmentOptions.Right;
        scrollHint.gameObject.SetActive(scroll.content.rect.height > scroll.viewport.rect.height);

        Button returnButton = CreateButton("Return Button", root, font, returnLabel, paperTheme);
        RectTransform buttonRect = returnButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(190f, -470f);
        buttonRect.sizeDelta = new Vector2(400f, 76f);
        Button retryButton = CreateButton("Try Again Button", root, font, "Try Again", paperTheme);
        RectTransform retryRect = retryButton.GetComponent<RectTransform>();
        retryRect.anchorMin = new Vector2(0.5f, 0.5f);
        retryRect.anchorMax = new Vector2(0.5f, 0.5f);
        retryRect.pivot = new Vector2(0.5f, 0.5f);
        retryRect.anchoredPosition = new Vector2(-224f, -470f);
        retryRect.sizeDelta = new Vector2(300f, 76f);
        returnButton.onClick.AddListener(() =>
        {
            DisabledButtonFeedback.SetInteractable(returnButton, false);
            DisabledButtonFeedback.SetInteractable(retryButton, false);
            onReturn?.Invoke();
        });
        retryButton.onClick.AddListener(() =>
        {
            if (!retryButton.IsInteractable())
            {
                return;
            }
            DisabledButtonFeedback.SetInteractable(returnButton, false);
            DisabledButtonFeedback.SetInteractable(retryButton, false);
            if (onRetry != null)
            {
                onRetry.Invoke();
            }
            else
            {
                FleeApiClient.ResetClassroomSession();
                DoorSceneTransition.LoadScene("Classroom and Movement", "Assets/Scenes/Classroom and Movement.unity");
            }
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
        string teacherNoteLabel,
        bool includeTeacherNote = true)
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

                builder.Append('\n');
            }
        }

        if (includeTeacherNote && teacherResult != null && !string.IsNullOrWhiteSpace(teacherResult.TeacherRemark))
        {
            builder.Append("<b>").Append(teacherNoteLabel).Append(":</b>\n");
            builder.Append(teacherResult.TeacherRemark.Trim());
        }

        return builder.ToString();
    }

    private static void BuildDetailRows(
        RectTransform content,
        IReadOnlyList<FleePupilSession> pupils,
        FleeTeacherSceneResult teacherResult,
        string teacherNoteLabel,
        TMP_FontAsset font,
        Color dividerColor)
    {
        bool hasRow = false;
        if (pupils != null)
        {
            for (int index = 0; index < pupils.Count; index++)
            {
                if (pupils[index] == null)
                {
                    continue;
                }
                if (hasRow)
                {
                    CreateDetailDivider(content, dividerColor);
                }
                CreateText("Student " + (index + 1), content, font,
                    BuildDetails(new[] { pupils[index] }, teacherResult, teacherNoteLabel, false).Trim(),
                    20f, FontStyles.Normal);
                hasRow = true;
            }
        }
        if (teacherResult != null && !string.IsNullOrWhiteSpace(teacherResult.TeacherRemark))
        {
            if (hasRow)
            {
                CreateDetailDivider(content, dividerColor);
            }
            CreateText("Teacher Note", content, font,
                "<b>" + teacherNoteLabel + ":</b>\n" + teacherResult.TeacherRemark.Trim(),
                20f, FontStyles.Normal);
        }
    }

    private static void CreateDetailDivider(RectTransform parent, Color tint)
    {
        Image divider = CreateImage("Student Divider", parent, new Color(tint.r, tint.g, tint.b, 0.28f));
        divider.raycastTarget = false;
        LayoutElement layout = divider.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 2f;
        layout.preferredHeight = 2f;
    }

    private static void CreateDivider(RectTransform parent, string name, float position, Color tint)
    {
        Image divider = CreateImage(name, parent, new Color(tint.r, tint.g, tint.b, 0.28f));
        divider.raycastTarget = false;
        SetRect(divider.rectTransform, new Vector2(72f, position), new Vector2(-72f, position - 2f));
    }

    public static string GetLetterRating(int score)
    {
        ReportRatingSettings settings = ReportRatingSettings.Load();
        return settings != null ? settings.Resolve(Mathf.Clamp(score, 0, 100))?.grade ?? "—" : "—";
    }

    private static void CreateRatingBadge(RectTransform parent, TMP_FontAsset font, int score)
    {
        GameObject badge = new GameObject("AraBOT Rating Badge", typeof(RectTransform), typeof(CanvasRenderer), typeof(ReportRatingBadge));
        badge.transform.SetParent(parent, false);
        RectTransform rect = badge.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-48f, -36f);
        rect.sizeDelta = new Vector2(130f, 130f);
        ReportRatingBadge circle = badge.GetComponent<ReportRatingBadge>();
        circle.color = InkColor;
        circle.raycastTarget = false;
        TMP_Text letter = CreateText("Letter Rating", rect, font, GetLetterRating(score), 48f, FontStyles.Bold);
        SetRect(letter.rectTransform, new Vector2(10f, -12f), new Vector2(-10f, -86f));
        letter.color = Color.white;
        letter.alignment = TextAlignmentOptions.Center;
        ReportRatingSettings settings = ReportRatingSettings.Load();
        ReportRatingSettings.RatingBand band = settings != null ? settings.Resolve(score) : null;
        TMP_Text caption = CreateText("Rating Caption", rect, font, band != null ? band.PickText() : "ANSWER SUCCESS", 12f, FontStyles.Normal);
        SetRect(caption.rectTransform, new Vector2(14f, -80f), new Vector2(-14f, -108f));
        caption.color = Color.white;
        caption.alignment = TextAlignmentOptions.Center;
        caption.enableAutoSizing = true;
        caption.fontSizeMin = 9f;
        caption.fontSizeMax = 12f;
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
        // Stencil masking follows the rotated paper; RectMask2D can cut across
        // student text because its clipping rectangle is aligned to the canvas.
        viewportImage.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        GameObject details = new GameObject("Details", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform detailsRect = details.GetComponent<RectTransform>();
        detailsRect.SetParent(viewportRect, false);
        detailsRect.anchorMin = new Vector2(0f, 1f);
        detailsRect.anchorMax = new Vector2(1f, 1f);
        detailsRect.pivot = new Vector2(0.5f, 1f);
        detailsRect.anchoredPosition = Vector2.zero;
        detailsRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = details.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 22, 22);
        layout.spacing = 16f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        details.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewportImage.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = detailsRect;
        UiScrollSettings.Apply(scroll);
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
            paperTheme != null ? paperTheme.RobotButton : null,
            1f);
        if (paperTheme != null)
        {
            paperTheme.ApplyButtonSlicing(image);
        }
        AddPaperShadow(image, new Vector2(5f, -6f), 0.2f);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        image.gameObject.AddComponent<ButtonJuice>();
        button.transition = Selectable.Transition.None;
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
        int lineCount = Mathf.FloorToInt((paperRect.sizeDelta.y - 420f) / 34f);
        for (int index = 0; index < lineCount; index++)
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
            lineRect.anchoredPosition = new Vector2(0f, -388f - (index * 34f));
            lineRect.sizeDelta = new Vector2(-144f, 2f);
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
        text.textWrappingMode = TextWrappingModes.Normal;
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

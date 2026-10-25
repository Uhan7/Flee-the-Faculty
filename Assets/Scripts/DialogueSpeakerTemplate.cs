using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation-only prefab. Its preview text is never used as dialogue.</summary>
public sealed class DialogueSpeakerTemplate : MonoBehaviour
{
    public const string PreviewText = "Lorem ipsum dolor sit amet, lorem ipsum dolor sit amet.";

    [SerializeField] private TMP_Text speaker;
    [SerializeField] private TMP_Text body;
    [SerializeField] private Image box;
    [SerializeField] private Image nameBox;
    [SerializeField] private Image arrow;

    public void Configure(TMP_Text speakerPreview, TMP_Text bodyPreview, Image boxPreview, Image namePreview, Image arrowPreview)
    {
        speaker = speakerPreview;
        body = bodyPreview;
        box = boxPreview;
        nameBox = namePreview;
        arrow = arrowPreview;
    }

    public void Apply(TMP_Text targetSpeaker, TMP_Text targetBody, RectTransform viewport, Image targetBox, Image targetName, Image targetArrow)
    {
        CopyText(speaker, targetSpeaker);
        CopyText(body, targetBody);
        CopyRect(speaker != null ? speaker.rectTransform : null, targetSpeaker != null ? targetSpeaker.rectTransform : null);
        CopyRect(body != null ? body.rectTransform : null, viewport);
        CopyImage(box, targetBox);
        CopyImage(nameBox, targetName);
        CopyImage(arrow, targetArrow);
    }

    private static void CopyText(TMP_Text source, TMP_Text target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.font = source.font;
        target.fontSharedMaterial = source.fontSharedMaterial;
        target.fontSize = source.fontSize;
        target.enableAutoSizing = source.enableAutoSizing;
        target.fontSizeMin = source.fontSizeMin;
        target.fontSizeMax = source.fontSizeMax;
        target.fontStyle = source.fontStyle;
        target.color = source.color;
        target.alignment = source.alignment;
        target.characterSpacing = source.characterSpacing;
        target.wordSpacing = source.wordSpacing;
        target.lineSpacing = source.lineSpacing;
        target.paragraphSpacing = source.paragraphSpacing;
        target.margin = source.margin;
        target.textWrappingMode = source.textWrappingMode;
    }

    private static void CopyImage(Image source, Image target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.sprite = source.sprite;
        target.type = source.type;
        target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        CopyRect(source.rectTransform, target.rectTransform);
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }
}

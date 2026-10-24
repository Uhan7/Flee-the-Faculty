using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Reusable slide-up window with a full-canvas modal backdrop.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public sealed class MenuPopupMotion : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float duration = 0.3f;
    [SerializeField, Min(0f)] private float distance = 900f;
    private RectTransform rect;
    private CanvasGroup group;
    private GameObject backdrop;
    private Vector2 restingPosition;
    private Coroutine animation;

    public static float Ease(float progress)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
    }

    public static IEnumerator Slide(
        RectTransform incoming,
        Vector2 from,
        Vector2 to,
        float seconds,
        AnimationCurve curve = null,
        RectTransform outgoing = null)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, seconds));
            float eased = curve != null ? curve.Evaluate(progress) : Ease(progress);
            incoming.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
            if (outgoing != null)
            {
                outgoing.anchoredPosition = Vector2.LerpUnclamped(to, from, eased);
            }
            yield return null;
        }
        incoming.anchoredPosition = to;
        if (outgoing != null)
        {
            outgoing.anchoredPosition = from;
        }
    }

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        group = GetComponent<CanvasGroup>();
        restingPosition = rect.anchoredPosition;
        AddCloseButton(rect, Close);
        backdrop = new GameObject(name + " Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdrop.layer = gameObject.layer;
        RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
        backdropRect.SetParent(transform.parent, false);
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        backdrop.SetActive(false);
    }

    public static void AddCloseButton(RectTransform panel, UnityEngine.Events.UnityAction close)
    {
        if (panel.Find("Window Close") != null)
        {
            return;
        }
        GameObject control = new GameObject("Window Close", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        control.layer = panel.gameObject.layer;
        RectTransform controlRect = control.GetComponent<RectTransform>();
        controlRect.SetParent(panel, false);
        controlRect.anchorMin = Vector2.one;
        controlRect.anchorMax = Vector2.one;
        controlRect.pivot = Vector2.one;
        controlRect.anchoredPosition = new Vector2(0f, 64f);
        controlRect.sizeDelta = new Vector2(44f, 44f);
        Image image = control.GetComponent<Image>();
        PaperUiTheme theme = PaperUiTheme.Load();
        image.sprite = theme != null ? theme.RobotButton : null;
        image.type = Image.Type.Sliced;
        if (theme != null)
        {
            theme.ApplyButtonSlicing(image);
        }
        Button button = control.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(close);
        GameObject label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        label.layer = panel.gameObject.layer;
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.SetParent(controlRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        TMP_Text text = label.GetComponent<TMP_Text>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = "X";
        text.fontSize = 22f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.075f, 0.12f, 0.2f, 1f);
        text.raycastTarget = false;
        control.AddComponent<ButtonJuice>();
    }

    private void OnEnable()
    {
        backdrop.transform.SetAsLastSibling();
        transform.SetAsLastSibling();
        backdrop.SetActive(true);
        animation = StartCoroutine(Animate(true));
    }

    public void Close()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }
        if (animation != null)
        {
            StopCoroutine(animation);
        }
        animation = StartCoroutine(Animate(false));
    }

    private IEnumerator Animate(bool opening)
    {
        group.interactable = false;
        group.blocksRaycasts = false;
        Vector2 start = opening ? restingPosition + Vector2.down * distance : rect.anchoredPosition;
        Vector2 end = opening ? restingPosition : restingPosition + Vector2.down * distance;
        yield return Slide(rect, start, end, duration);
        group.interactable = opening;
        group.blocksRaycasts = opening;
        animation = null;
        if (!opening)
        {
            gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        if (animation != null)
        {
            StopCoroutine(animation);
            animation = null;
        }
        if (rect != null)
        {
            rect.anchoredPosition = restingPosition;
        }
        if (backdrop != null)
        {
            backdrop.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (backdrop != null)
        {
            Destroy(backdrop);
        }
    }
}

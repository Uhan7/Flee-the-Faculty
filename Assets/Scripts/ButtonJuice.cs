using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button), typeof(RectTransform))]
public sealed class ButtonJuice : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    ISelectHandler,
    IDeselectHandler
{
    private enum MotionStyle
    {
        ScaleOnly,
        ScaleAndRotate
    }

    [Header("Motion")]
    [SerializeField] private MotionStyle motionStyle = MotionStyle.ScaleOnly;
    [SerializeField, Min(1f)] private float tweenSpeed = 18f;
    [SerializeField, Min(1f)] private float hoverScale = 1.06f;
    [SerializeField, Range(0.5f, 1f)] private float pressedScale = 0.92f;
    [SerializeField, Min(0f)] private float clickPunch = 0.12f;
    [SerializeField, Min(0.01f)] private float clickDuration = 0.22f;

    [Header("Rotation")]
    [Tooltip("When enabled, the button chooses a new clockwise or counter-clockwise "
        + "hover angle every time it is highlighted.")]
    [SerializeField] private bool useRandomHoverRotation = true;
    [Tooltip("Used when random rotation is disabled. Positive values rotate counter-clockwise.")]
    [SerializeField] private float hoverRotation = -1.5f;
    [Tooltip("Smallest absolute angle a random rotation may choose. This creates the gap around zero.")]
    [SerializeField, Min(0f)] private float minimumRandomRotation = 1.5f;
    [Tooltip("Largest absolute angle a random rotation may choose.")]
    [SerializeField, Min(0f)] private float maximumRandomRotation = 3f;
    [Tooltip("Maximum click-wobble angle. Each click randomly uses half to all of this angle.")]
    [SerializeField, Min(0f)] private float clickWobbleAngle = 3f;

    [Header("Colour")]
    [SerializeField, Range(0f, 1f)] private float hoverBrightness = 0.12f;
    [SerializeField, Range(0f, 1f)] private float pressedDarkness = 0.1f;

    private Button button;
    private RectTransform rectTransform;
    private Vector3 baseScale;
    private Quaternion baseRotation;
    private float clickElapsed = float.MaxValue;
    private float currentHoverRotation;
    private float currentClickWobbleAngle;
    private bool isHovered;
    private bool isPressed;

    public void UsePaperCardMotion()
    {
        hoverScale = 1.015f;
        pressedScale = 0.98f;
        clickPunch = 0.015f;
    }

    private void Awake()
    {
        button = GetComponent<Button>();
        rectTransform = GetComponent<RectTransform>();
        SetPivotToCenter();
        baseScale = rectTransform.localScale;
        baseRotation = rectTransform.localRotation;
        if (GetComponent<DisabledButtonFeedback>() == null)
        {
            gameObject.AddComponent<DisabledButtonFeedback>();
        }
    }

    private void OnEnable()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }

        isHovered = false;
        isPressed = false;
        clickElapsed = clickDuration;
        currentHoverRotation = 0f;
        currentClickWobbleAngle = 0f;
    }

    private void OnDisable()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
        }

        RestoreAppearance();
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
        }
    }

    private void Update()
    {
        if (rectTransform == null)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        if (button != null && !button.IsInteractable())
        {
            isHovered = false;
            isPressed = false;
        }
        clickElapsed += deltaTime;
        float clickProgress = Mathf.Clamp01(clickElapsed / Mathf.Max(0.01f, clickDuration));
        float punch = Mathf.Sin(clickProgress * Mathf.PI) * clickPunch * (1f - clickProgress);
        float interactionScale = isPressed ? pressedScale : isHovered ? hoverScale : 1f;
        Vector3 desiredScale = baseScale * (interactionScale + punch);
        rectTransform.localScale = Vector3.Lerp(
            rectTransform.localScale,
            desiredScale,
            1f - Mathf.Exp(-tweenSpeed * deltaTime));

        bool shouldRotate = motionStyle == MotionStyle.ScaleAndRotate;
        float wobble = shouldRotate
            ? Mathf.Sin(clickProgress * Mathf.PI * 2.4f)
                * currentClickWobbleAngle
                * (1f - clickProgress)
            : 0f;
        float tilt = shouldRotate && isHovered && !isPressed ? currentHoverRotation : 0f;
        Quaternion desiredRotation = baseRotation * Quaternion.Euler(0f, 0f, tilt + wobble);
        rectTransform.localRotation = Quaternion.Lerp(
            rectTransform.localRotation,
            desiredRotation,
            1f - Mathf.Exp(-tweenSpeed * deltaTime));

    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button == null || !button.interactable)
        {
            isHovered = false;
            return;
        }

        if (!isHovered)
        {
            currentHoverRotation = ResolveHoverRotation();
            ButtonJuiceRuntime.PlayHover();
        }

        isHovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        isPressed = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = button != null && button.interactable;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (button == null || !button.interactable)
        {
            isHovered = false;
            return;
        }

        if (!isHovered)
        {
            currentHoverRotation = ResolveHoverRotation();
            ButtonJuiceRuntime.PlayHover();
        }

        isHovered = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        isHovered = false;
        isPressed = false;
    }

    private void HandleClick()
    {
        clickElapsed = 0f;
        currentClickWobbleAngle = ResolveRandomSignedMagnitude(
            clickWobbleAngle * 0.5f,
            clickWobbleAngle);
        isPressed = false;
        ButtonJuiceRuntime.PlayClick();
    }

    private void RestoreAppearance()
    {
        isHovered = false;
        isPressed = false;
        if (rectTransform != null)
        {
            rectTransform.localScale = baseScale;
            rectTransform.localRotation = baseRotation;
        }

    }

    private float ResolveHoverRotation()
    {
        if (!useRandomHoverRotation)
        {
            return hoverRotation;
        }

        float minimum = Mathf.Min(minimumRandomRotation, maximumRandomRotation);
        float maximum = Mathf.Max(minimumRandomRotation, maximumRandomRotation);
        return ResolveRandomSignedMagnitude(minimum, maximum);
    }

    private static float ResolveRandomSignedMagnitude(float minimum, float maximum)
    {
        float magnitude = Random.Range(
            Mathf.Min(Mathf.Abs(minimum), Mathf.Abs(maximum)),
            Mathf.Max(Mathf.Abs(minimum), Mathf.Abs(maximum)));
        return magnitude * (Random.value < 0.5f ? -1f : 1f);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maximumRandomRotation < minimumRandomRotation)
        {
            maximumRandomRotation = minimumRandomRotation;
        }
    }
#endif

    private void SetPivotToCenter()
    {
        if (rectTransform == null || rectTransform.pivot == new Vector2(0.5f, 0.5f))
        {
            return;
        }

        Vector2 size = rectTransform.rect.size;
        Vector2 previousPivot = rectTransform.pivot;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition += new Vector2(
            (0.5f - previousPivot.x) * size.x,
            (0.5f - previousPivot.y) * size.y);
    }
}

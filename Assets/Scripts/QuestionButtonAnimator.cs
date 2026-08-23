using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class QuestionButtonAnimator : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    ISelectHandler,
    IDeselectHandler
{
    [Header("References")]
    [SerializeField] private Button button;
    [SerializeField] private Graphic targetGraphic;

    [Header("Appear")]
    [SerializeField, Min(0.01f)] private float appearDuration = 0.28f;
    [SerializeField, Range(0.1f, 1f)] private float hiddenScaleMultiplier = 0.62f;
    [SerializeField, Min(0f)] private float appearBounceStrength = 0.28f;

    [Header("Float")]
    [SerializeField, Min(0f)] private float bobAmplitude = 0.12f;
    [SerializeField, Min(0.1f)] private float bobCyclesPerSecond = 1.55f;

    [Header("Interaction")]
    [SerializeField, Min(1f)] private float positionLerpSpeed = 12f;
    [SerializeField, Min(1f)] private float scaleLerpSpeed = 16f;
    [SerializeField, Min(1f)] private float rotationLerpSpeed = 14f;
    [SerializeField, Min(1f)] private float colorLerpSpeed = 16f;
    [SerializeField, Min(1f)] private float hoverScaleMultiplier = 1.08f;
    [SerializeField, Range(0.1f, 1f)] private float pressedScaleMultiplier = 0.9f;
    [SerializeField, Min(0f)] private float hoverLift = 0.08f;
    [SerializeField, Min(0f)] private float pressedSink = 0.05f;
    [SerializeField] private float hoverTilt = -4f;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hoverColor = new Color(1f, 0.97f, 0.76f, 1f);
    [SerializeField] private Color pressedColor = new Color(1f, 0.86f, 0.56f, 1f);

    private RectTransform rectTransform;
    private Vector2 baseAnchoredPosition;
    private Vector3 baseScale;
    private Quaternion baseRotation;
    private float showStartTime;
    private float bobOffsetSeed;
    private bool isHovered;
    private bool isPressed;

    private void Reset()
    {
        rectTransform = GetComponent<RectTransform>();
        button = GetComponent<Button>();
        targetGraphic = GetComponent<Graphic>();
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (targetGraphic == null)
        {
            targetGraphic = GetComponent<Graphic>();
        }

        if (button != null)
        {
            button.transition = Selectable.Transition.None;
        }

        baseAnchoredPosition = rectTransform.anchoredPosition;
        baseScale = rectTransform.localScale;
        baseRotation = rectTransform.localRotation;
        bobOffsetSeed = Random.value * Mathf.PI * 2f;

        if (targetGraphic != null)
        {
            targetGraphic.color = normalColor;
        }
    }

    private void OnEnable()
    {
        showStartTime = Time.unscaledTime;
        isPressed = false;
        isHovered = false;

        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = baseAnchoredPosition;
            rectTransform.localScale = baseScale * hiddenScaleMultiplier;
            rectTransform.localRotation = baseRotation;
        }

        if (targetGraphic != null)
        {
            targetGraphic.color = normalColor;
        }
    }

    private void OnDisable()
    {
        isPressed = false;
        isHovered = false;

        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = baseAnchoredPosition;
            rectTransform.localScale = baseScale;
            rectTransform.localRotation = baseRotation;
        }

        if (targetGraphic != null)
        {
            targetGraphic.color = normalColor;
        }
    }

    private void Update()
    {
        if (rectTransform == null)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        float bobWave = Mathf.Sin((Time.unscaledTime + bobOffsetSeed) * bobCyclesPerSecond * Mathf.PI * 2f);
        float bobOffset = bobWave * bobAmplitude;
        float stateOffset = isHovered ? hoverLift : 0f;
        if (isPressed)
        {
            stateOffset -= pressedSink;
        }

        Vector2 targetPosition = baseAnchoredPosition + Vector2.up * (bobOffset + stateOffset);
        rectTransform.anchoredPosition = Vector2.Lerp(
            rectTransform.anchoredPosition,
            targetPosition,
            GetLerpFactor(positionLerpSpeed, deltaTime));

        float appearProgress = Mathf.Clamp01((Time.unscaledTime - showStartTime) / appearDuration);
        float appearScale = Mathf.Lerp(hiddenScaleMultiplier, 1f, EaseOutCubic(appearProgress));
        float bounceScale = 1f + Mathf.Sin(appearProgress * Mathf.PI) * appearBounceStrength * (1f - appearProgress);

        float interactionScale = 1f;
        if (isPressed)
        {
            interactionScale *= pressedScaleMultiplier;
        }
        else if (isHovered)
        {
            interactionScale *= hoverScaleMultiplier;
        }

        Vector3 targetScale = baseScale * (appearScale * bounceScale * interactionScale);
        rectTransform.localScale = Vector3.Lerp(
            rectTransform.localScale,
            targetScale,
            GetLerpFactor(scaleLerpSpeed, deltaTime));

        float targetAngle = 0f;
        if (isHovered && !isPressed)
        {
            targetAngle = hoverTilt;
        }

        Quaternion desiredRotation = baseRotation * Quaternion.Euler(0f, 0f, targetAngle);
        rectTransform.localRotation = Quaternion.Lerp(
            rectTransform.localRotation,
            desiredRotation,
            GetLerpFactor(rotationLerpSpeed, deltaTime));

        if (targetGraphic != null)
        {
            Color targetColor = normalColor;
            if (isPressed)
            {
                targetColor = pressedColor;
            }
            else if (isHovered)
            {
                targetColor = hoverColor;
            }

            targetGraphic.color = Color.Lerp(
                targetGraphic.color,
                targetColor,
                GetLerpFactor(colorLerpSpeed, deltaTime));
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        isPressed = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        isHovered = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        isHovered = false;
        isPressed = false;
    }

    private static float EaseOutCubic(float value)
    {
        float inverted = 1f - value;
        return 1f - (inverted * inverted * inverted);
    }

    private static float GetLerpFactor(float speed, float deltaTime)
    {
        return 1f - Mathf.Exp(-speed * deltaTime);
    }
}

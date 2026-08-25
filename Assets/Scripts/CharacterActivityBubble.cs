using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CharacterActivityBubble : MonoBehaviour
{
    private const int DotCount = 3;

    [Header("References")]
    [SerializeField] private DialogueActor actor;
    [SerializeField] private GameObject bubbleRoot;
    [SerializeField] private RectTransform dotContainer;
    [SerializeField] private Sprite dotSprite;

    [Header("Dot Layout")]
    [SerializeField] private Color dotColor = Color.white;
    [SerializeField, Min(0.01f)] private float dotSize = 0.12f;
    [SerializeField, Min(0f)] private float dotSpacing = 0.18f;
    [SerializeField] private Vector2 dotsOffset = new Vector2(0f, 0.08f);

    [Header("Animation")]
    [SerializeField, Min(0.01f)] private float bounceHeight = 0.09f;
    [SerializeField, Min(0.1f)] private float bounceCyclesPerSecond = 1.8f;
    [SerializeField, Range(0f, 1f)] private float dotPhaseOffset = 0.18f;
    [SerializeField, Range(0f, 1f)] private float minimumDotScale = 0.82f;
    [SerializeField] private bool showWhileSpeaking = true;

    private readonly RectTransform[] dots = new RectTransform[DotCount];
    private readonly Vector2[] dotBasePositions = new Vector2[DotCount];
    private DialogueManager dialogueManager;
    private bool manualThinking;
    private bool currentSpeaker;

    private void Awake()
    {
        ResolveReferences();
        BuildDotsIfNeeded();
        ApplyVisibility(false);
    }

    private void OnEnable()
    {
        dialogueManager = DialogueManager.GetOrCreate();
        if (dialogueManager != null)
        {
            dialogueManager.LineChanged += HandleLineChanged;
            dialogueManager.DialogueEnded += HandleDialogueEnded;
            HandleLineChanged(dialogueManager.ActiveLine, -1);
        }
    }

    private void OnDisable()
    {
        if (dialogueManager != null)
        {
            dialogueManager.LineChanged -= HandleLineChanged;
            dialogueManager.DialogueEnded -= HandleDialogueEnded;
        }

        currentSpeaker = false;
        manualThinking = false;
        ApplyVisibility(false);
    }

    private void Update()
    {
        bool speaking = showWhileSpeaking
            && currentSpeaker
            && dialogueManager != null
            && dialogueManager.IsPlaying
            && dialogueManager.IsTyping;
        bool shouldShow = manualThinking || speaking;
        ApplyVisibility(shouldShow);

        if (shouldShow)
        {
            AnimateDots();
        }
    }

    public void SetThinking(bool visible)
    {
        manualThinking = visible;
        ApplyVisibility(manualThinking || IsSpeaking());
    }

    private void ResolveReferences()
    {
        if (actor == null)
        {
            actor = GetComponent<DialogueActor>();
        }

        if (dotContainer == null && bubbleRoot != null)
        {
            dotContainer = bubbleRoot.transform as RectTransform;
        }

        if (bubbleRoot != null && bubbleRoot.TryGetComponent(out Button bubbleButton))
        {
            bubbleButton.interactable = false;
        }
    }

    private void BuildDotsIfNeeded()
    {
        if (dotContainer == null || dotSprite == null)
        {
            return;
        }

        float totalWidth = dotSpacing * (DotCount - 1);
        for (int index = 0; index < DotCount; index++)
        {
            GameObject dotObject = new GameObject("Thinking Dot " + (index + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform dotRect = dotObject.GetComponent<RectTransform>();
            dotRect.SetParent(dotContainer, false);
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = Vector2.one * dotSize;
            dotRect.anchoredPosition = dotsOffset + new Vector2((index * dotSpacing) - (totalWidth * 0.5f), 0f);

            Image dotImage = dotObject.GetComponent<Image>();
            dotImage.sprite = dotSprite;
            dotImage.color = dotColor;
            dotImage.preserveAspect = true;
            dotImage.raycastTarget = false;

            dots[index] = dotRect;
            dotBasePositions[index] = dotRect.anchoredPosition;
        }
    }

    private void AnimateDots()
    {
        float cycle = Time.unscaledTime * bounceCyclesPerSecond;
        for (int index = 0; index < dots.Length; index++)
        {
            RectTransform dot = dots[index];
            if (dot == null)
            {
                continue;
            }

            float phase = Mathf.Repeat(cycle - (index * dotPhaseOffset), 1f);
            float bounce = Mathf.Sin(phase * Mathf.PI);
            bounce *= bounce;
            dot.anchoredPosition = dotBasePositions[index] + (Vector2.up * bounce * bounceHeight);
            dot.localScale = Vector3.one * Mathf.Lerp(minimumDotScale, 1f, bounce);
        }
    }

    private void HandleLineChanged(IDialogueLine line, int _)
    {
        currentSpeaker = line != null && IsActorReference(line.SpeakerReference);
    }

    private void HandleDialogueEnded(IDialogueSequence _)
    {
        currentSpeaker = false;
        ApplyVisibility(manualThinking);
    }

    private bool IsActorReference(Object speakerReference)
    {
        if (speakerReference == null || actor == null)
        {
            return false;
        }

        if (speakerReference == actor || speakerReference == actor.gameObject)
        {
            return true;
        }

        Component speakerComponent = speakerReference as Component;
        return speakerComponent != null && speakerComponent.GetComponent<DialogueActor>() == actor;
    }

    private bool IsSpeaking()
    {
        return showWhileSpeaking
            && currentSpeaker
            && dialogueManager != null
            && dialogueManager.IsPlaying
            && dialogueManager.IsTyping;
    }

    private void ApplyVisibility(bool visible)
    {
        if (bubbleRoot != null && bubbleRoot.activeSelf != visible)
        {
            bubbleRoot.SetActive(visible);
        }
    }
}

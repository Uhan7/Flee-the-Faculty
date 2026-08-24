using UnityEngine;

[DisallowMultipleComponent]
public sealed class DialogueSpeakerMouth : MonoBehaviour
{
    [SerializeField] private DialogueActor speaker;
    [SerializeField] private Transform mouth;
    [SerializeField, Min(0.1f)] private float talkCyclesPerSecond = 5.5f;
    [SerializeField, Min(0f)] private float leanAngle = 12f;
    [SerializeField, Min(0.1f)] private float closedScaleY = 0.8f;
    [SerializeField, Min(0.1f)] private float openScaleY = 1.8f;
    [SerializeField, Min(0.1f)] private float wideScaleX = 1.02f;
    [SerializeField, Min(0.1f)] private float narrowScaleX = 0.95f;
    [SerializeField, Min(1f)] private float smoothing = 18f;

    private DialogueManager dialogueManager;
    private Vector3 baseScale;
    private Quaternion baseRotation;
    private Quaternion talkingBaseRotation;
    private bool isTalking;

    private void Awake()
    {
        if (mouth == null)
        {
            mouth = transform;
        }

        baseScale = mouth.localScale;
        baseRotation = mouth.localRotation;
        Vector3 baseEulerAngles = mouth.localEulerAngles;
        talkingBaseRotation = Quaternion.Euler(baseEulerAngles.x, baseEulerAngles.y, 0f);
    }

    private void OnEnable()
    {
        dialogueManager = DialogueManager.GetOrCreate();
        if (dialogueManager != null)
        {
            dialogueManager.LineChanged += HandleLineChanged;
            dialogueManager.DialogueEnded += HandleDialogueEnded;

            if (dialogueManager.IsPlaying)
            {
                HandleLineChanged(dialogueManager.ActiveLine, -1);
            }
        }
    }

    private void OnDisable()
    {
        if (dialogueManager != null)
        {
            dialogueManager.LineChanged -= HandleLineChanged;
            dialogueManager.DialogueEnded -= HandleDialogueEnded;
        }

        isTalking = false;
        ResetMouthPose();
    }

    private void Update()
    {
        if (mouth == null)
        {
            return;
        }

        if (!isTalking)
        {
            mouth.localScale = Vector3.Lerp(mouth.localScale, baseScale, GetLerpFactor(Time.unscaledDeltaTime));
            mouth.localRotation = Quaternion.Lerp(mouth.localRotation, baseRotation, GetLerpFactor(Time.unscaledDeltaTime));
            return;
        }

        float wave = Mathf.Sin(Time.unscaledTime * talkCyclesPerSecond * Mathf.PI * 2f);
        float talkBlend = (wave + 1f) * 0.5f;

        Vector3 targetScale = new Vector3(
            baseScale.x * Mathf.Lerp(wideScaleX, narrowScaleX, talkBlend),
            baseScale.y * Mathf.Lerp(closedScaleY, openScaleY, talkBlend),
            baseScale.z);

        Quaternion targetRotation = talkingBaseRotation * Quaternion.Euler(0f, 0f, wave * leanAngle);

        mouth.localScale = Vector3.Lerp(mouth.localScale, targetScale, GetLerpFactor(Time.unscaledDeltaTime));
        mouth.localRotation = Quaternion.Lerp(mouth.localRotation, targetRotation, GetLerpFactor(Time.unscaledDeltaTime));
    }

    private void HandleLineChanged(IDialogueLine line, int _)
    {
        isTalking = line != null && line.SpeakerReference == speaker;
    }

    private void HandleDialogueEnded(IDialogueSequence _)
    {
        isTalking = false;
    }

    private void ResetMouthPose()
    {
        if (mouth == null)
        {
            return;
        }

        mouth.localScale = baseScale;
        mouth.localRotation = baseRotation;
    }

    private float GetLerpFactor(float deltaTime)
    {
        return 1f - Mathf.Exp(-smoothing * deltaTime);
    }
}

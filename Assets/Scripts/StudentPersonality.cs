using UnityEngine;

[DisallowMultipleComponent]
public sealed class StudentPersonality : MonoBehaviour
{
    [SerializeField] private DialogueActor actor;
    [SerializeField, TextArea(2, 5)] private string explanationNeed;
    [SerializeField, TextArea(2, 6)] private string quirk;
    [SerializeField] private AudioClip voiceClip;
    [SerializeField] private AudioSource voiceAudioSource;
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;

    public DialogueActor Actor => actor != null ? actor : GetComponent<DialogueActor>();
    public string StudentName => Actor != null && !string.IsNullOrWhiteSpace(Actor.DisplayName) ? Actor.DisplayName : gameObject.name;
    public string ExplanationNeed => explanationNeed ?? string.Empty;
    public string Quirk => quirk ?? string.Empty;
    public AudioClip VoiceClip => voiceClip;

    public string BuildPromptContext()
    {
        string name = StudentName;
        string need = string.IsNullOrWhiteSpace(ExplanationNeed) ? string.Empty : $"Explanation need: {ExplanationNeed.Trim()}";
        string quirkSummary = string.IsNullOrWhiteSpace(Quirk) ? string.Empty : $"Quirk: {Quirk.Trim()}";

        if (string.IsNullOrEmpty(need))
        {
            return string.IsNullOrEmpty(quirkSummary) ? name : $"{name}. {quirkSummary}";
        }

        return string.IsNullOrEmpty(quirkSummary)
            ? $"{name}. {need}"
            : $"{name}. {need} {quirkSummary}";
    }

    public void PlayVoiceTick()
    {
        if (voiceClip == null)
        {
            return;
        }

        AudioSource resolvedAudioSource = ResolveAudioSource();
        if (resolvedAudioSource == null)
        {
            return;
        }

        resolvedAudioSource.PlayOneShot(voiceClip, voiceVolume);
    }

    private void Reset()
    {
        actor = GetComponent<DialogueActor>();
        voiceAudioSource = GetComponent<AudioSource>();
    }

    private void OnValidate()
    {
        if (actor == null)
        {
            actor = GetComponent<DialogueActor>();
        }

        if (voiceAudioSource == null)
        {
            voiceAudioSource = GetComponent<AudioSource>();
        }
    }

    private AudioSource ResolveAudioSource()
    {
        if (voiceAudioSource != null)
        {
            return voiceAudioSource;
        }

        voiceAudioSource = GetComponent<AudioSource>();
        if (voiceAudioSource == null)
        {
            voiceAudioSource = gameObject.AddComponent<AudioSource>();
            voiceAudioSource.playOnAwake = false;
            voiceAudioSource.spatialBlend = 0f;
            voiceAudioSource.loop = false;
        }

        return voiceAudioSource;
    }
}

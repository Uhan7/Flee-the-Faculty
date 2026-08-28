using UnityEngine;

[DisallowMultipleComponent]
public sealed class DialogueActor : MonoBehaviour, IVoicedSpeaker
{
    [SerializeField] private string displayName = "Speaker";

    [Tooltip("Which recorded voice this Character speaks in. cast.py in the service decides which side of the split they are on.")]
    [SerializeField] private VoiceId voice = VoiceId.None;

    [Header("Dialogue Voice")]
    [SerializeField] private AudioClip[] voiceClips;
    [SerializeField] private AudioSource voiceAudioSource;
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;

    private AudioClip lastVoiceClip;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;

    public VoiceId Voice => voice;

    public bool TryGetStudentPersonality(out StudentPersonality personality)
    {
        personality = GetComponent<StudentPersonality>();
        return personality != null;
    }

    public string GetStudentPromptContext()
    {
        StudentPersonality attachedPersonality = GetComponent<StudentPersonality>();
        if (attachedPersonality != null)
        {
            return attachedPersonality.BuildPromptContext();
        }

        return string.Empty;
    }

    /// <summary>
    /// One syllable blip, played every few letters as the line types out.
    ///
    /// Silent while a baked clip is playing. The two are the same job done
    /// two ways, and the recorded line is the better one when it exists; the
    /// ticks stay as the fallback, which is what every line the model writes
    /// during an Encounter will need until the service speaks them.
    /// </summary>
    public void PlayVoiceTick()
    {
        if (DialogueVoicePlayer.Instance != null && DialogueVoicePlayer.Instance.IsSpeaking(this))
        {
            return;
        }

        AudioClip voiceClip = GetRandomVoiceClip();
        if (voiceClip == null)
        {
            return;
        }

        AudioSource resolvedAudioSource = ResolveAudioSource();
        if (resolvedAudioSource == null)
        {
            return;
        }

        lastVoiceClip = voiceClip;
        resolvedAudioSource.PlayOneShot(voiceClip, voiceVolume);
    }

    public void StopVoice()
    {
        if (voiceAudioSource != null)
        {
            voiceAudioSource.Stop();
        }
    }

    private void Reset()
    {
        voiceAudioSource = GetComponent<AudioSource>();
    }

    private void OnValidate()
    {
        if (voiceAudioSource == null)
        {
            voiceAudioSource = GetComponent<AudioSource>();
        }
    }

    private AudioClip GetRandomVoiceClip()
    {
        if (voiceClips == null || voiceClips.Length == 0)
        {
            return null;
        }

        int eligibleClipCount = 0;
        AudioClip fallbackClip = null;
        for (int index = 0; index < voiceClips.Length; index++)
        {
            AudioClip candidate = voiceClips[index];
            if (candidate == null)
            {
                continue;
            }

            fallbackClip ??= candidate;
            if (candidate != lastVoiceClip)
            {
                eligibleClipCount++;
            }
        }

        if (eligibleClipCount == 0)
        {
            return fallbackClip;
        }

        int selectedClipIndex = Random.Range(0, eligibleClipCount);
        for (int index = 0; index < voiceClips.Length; index++)
        {
            AudioClip candidate = voiceClips[index];
            if (candidate == null || candidate == lastVoiceClip)
            {
                continue;
            }

            if (selectedClipIndex == 0)
            {
                return candidate;
            }

            selectedClipIndex--;
        }

        return fallbackClip;
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

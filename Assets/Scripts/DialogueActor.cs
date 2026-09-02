using UnityEngine;

[DisallowMultipleComponent]
public sealed class DialogueActor : MonoBehaviour, IVoicedSpeaker
{
    [SerializeField] private string displayName = "Speaker";

    [Tooltip("Which recorded voice this Character speaks in. cast.py in the service decides which side of the split they are on.")]
    [SerializeField] private VoiceId voice = VoiceId.None;

    [Header("Dialogue Appearance")]
    [SerializeField] private bool useBrownDialogueStyle;

    [Header("Dialogue Voice")]
    [SerializeField] private AudioClip[] voiceClips;
    [SerializeField] private AudioSource voiceAudioSource;
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;

    private AudioClip lastVoiceClip;
    private string runtimeVoiceSlot = string.Empty;
    private VoiceId runtimeVoice = VoiceId.None;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public bool UsesBrownDialogueStyle => useBrownDialogueStyle;

    public VoiceId Voice => runtimeVoice == VoiceId.None ? voice : runtimeVoice;

    /// <summary>
    /// Which of the six slots the service speaks this Character in, or an empty
    /// string for a Character nobody has configured from the wire.
    /// </summary>
    public string VoiceSlot => runtimeVoiceSlot;

    /// <summary>
    /// Take this Character's voice from the Classroom rather than from the prefab.
    ///
    /// <c>cast.py</c> owns which of the six slots a Character speaks in and
    /// <c>rules.py</c> refuses to draw a Classroom where two Pupils share one, so
    /// the wire is the authority and the serialised <see cref="Voice"/> is the
    /// fallback for a scene nobody wired to a Classroom. Called by
    /// <c>StudentDialogueInteraction.ConfigureBackendPupil</c>.
    ///
    /// A Pupil speaks only lines the model wrote at turn time, so overriding the
    /// base voice here cannot orphan a baked clip: there are none to orphan.
    /// </summary>
    public void SetVoiceSlot(string wireSlot)
    {
        if (string.IsNullOrWhiteSpace(wireSlot))
        {
            runtimeVoiceSlot = string.Empty;
            runtimeVoice = VoiceId.None;
            return;
        }

        runtimeVoiceSlot = wireSlot.Trim();
        runtimeVoice = VoiceCatalog.TryParseWire(runtimeVoiceSlot, out VoiceId parsed)
            ? parsed
            : VoiceId.None;
    }

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
    /// Silent while this Character has a real voice on the line, and for the
    /// short wait while one is being synthesised. The two are the same job done
    /// two ways, and the spoken line is the better one when it exists; the ticks
    /// stay as the fallback for a line no voice reaches.
    /// </summary>
    public void PlayVoiceTick()
    {
        if (DialogueVoicePlayer.Instance != null && DialogueVoicePlayer.Instance.IsVoicing(this))
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
        resolvedAudioSource.PlayOneShot(
            voiceClip,
            voiceVolume * AudioSettingsStore.SfxVolume);
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

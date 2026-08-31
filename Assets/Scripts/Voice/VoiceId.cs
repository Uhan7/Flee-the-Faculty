using UnityEngine;

/// <summary>
/// The two voices a line is <em>baked</em> in: one girl and one boy.
///
/// This is not the same thing as the six voice slots. GDD 8.3 specifies six and
/// the service speaks all six, but they are rendered from two Piper models, and
/// this enum names the models. A baked clip's file name carries one of these
/// (<see cref="VoiceKey"/>), so widening it would rename every clip in
/// <c>Assets/Audio/Voices</c> and change nothing about how a Pupil sounds.
///
/// Which of the six a Pupil actually speaks in arrives on the wire, as
/// <c>ClassroomView.pupils[].voice</c>, and lives on
/// <see cref="IVoicedSpeaker.VoiceSlot"/> at run time. See
/// <see cref="ServiceVoiceSynthesizer"/> and ADR-0013 in the service repository.
/// </summary>
public enum VoiceId
{
    None = 0,
    Girl = 1,
    Boy = 2,
}

/// <summary>
/// How a voice is named on the wire, and how the six slots fold onto the two
/// base voices.
/// </summary>
public static class VoiceCatalog
{
    /// <summary>Names the baked clips use, and what the service calls a base voice.</summary>
    public const string GirlKey = "girl";

    public const string BoyKey = "boy";

    /// <summary>
    /// The name this voice is filed under, or an empty string for
    /// <see cref="VoiceId.None"/>.
    ///
    /// The service accepts this as a voice too, where it means the base voice
    /// with the child-register lift and no slot offset. That is exactly what
    /// authored dialogue is baked in, so an authored line and its baked clip
    /// sound the same whichever of the two produced it.
    /// </summary>
    public static string ToKey(VoiceId voice)
    {
        switch (voice)
        {
            case VoiceId.Girl: return GirlKey;
            case VoiceId.Boy: return BoyKey;
            default: return string.Empty;
        }
    }

    /// <summary>The inverse of <see cref="ToKey"/>, for reading a baked file name.</summary>
    public static bool TryParseKey(string key, out VoiceId voice)
    {
        switch (key)
        {
            case GirlKey: voice = VoiceId.Girl; return true;
            case BoyKey: voice = VoiceId.Boy; return true;
            default: voice = VoiceId.None; return false;
        }
    }

    /// <summary>
    /// Read the voice off whatever a dialogue line names as its speaker.
    ///
    /// A line's <c>SpeakerReference</c> is a <c>DialogueActor</c> on a prefab or
    /// a <c>DialogueSpeaker</c> asset, and both carry a voice. Anything else, or
    /// a line with no speaker at all, plays silent.
    /// </summary>
    public static VoiceId VoiceOf(Object speakerReference)
    {
        return speakerReference is IVoicedSpeaker speaker ? speaker.Voice : VoiceId.None;
    }

    /// <summary>
    /// Which of the six slots this speaker asks the service for.
    ///
    /// A Pupil in a real Encounter carries the slot <c>cast.py</c> pinned to
    /// their Character, put there by <c>StudentDialogueInteraction</c> when the
    /// Classroom arrives. Everyone else has none, and falls back to the base
    /// voice their baked clips use, which is what an authored line wants anyway.
    /// </summary>
    public static string SlotOf(Object speakerReference)
    {
        if (speakerReference is not IVoicedSpeaker speaker)
        {
            return string.Empty;
        }

        string slot = speaker.VoiceSlot;
        return string.IsNullOrWhiteSpace(slot) ? ToKey(speaker.Voice) : slot.Trim();
    }

    /// <summary>
    /// Fold a slot name from the wire onto a base voice. V1 to V3 are female and
    /// V4 to V6 are male, which is the same split <c>cast.py</c> holds as
    /// <c>FEMALE_VOICES</c> and <c>MALE_VOICES</c> and the same one
    /// <c>speech/slots.py</c> renders from.
    ///
    /// The client needs this only to decide which baked clips a Pupil could
    /// match. The slot itself goes to the service untouched, because the
    /// difference between V1 and V2 is the service's to make and folding it away
    /// here is what used to make three girls in one Classroom sound identical.
    /// </summary>
    public static bool TryParseWire(string wireValue, out VoiceId voice)
    {
        voice = VoiceId.None;
        if (string.IsNullOrWhiteSpace(wireValue))
        {
            return false;
        }

        switch (wireValue.Trim().ToUpperInvariant())
        {
            case "V1":
            case "V2":
            case "V3":
                voice = VoiceId.Girl;
                return true;
            case "V4":
            case "V5":
            case "V6":
                voice = VoiceId.Boy;
                return true;
            default:
                return TryParseKey(wireValue.Trim().ToLowerInvariant(), out voice);
        }
    }
}

/// <summary>
/// Anything a dialogue line can name as its speaker and that owns a voice.
/// </summary>
public interface IVoicedSpeaker
{
    /// <summary>Which of the two base voices this Character's clips are baked in.</summary>
    VoiceId Voice { get; }

    /// <summary>
    /// Which of the six slots the service should speak in, or an empty string to
    /// let <see cref="Voice"/> decide. Set from the wire for a Pupil in a real
    /// Encounter; empty for every authored speaker.
    /// </summary>
    string VoiceSlot { get; }
}

using UnityEngine;

/// <summary>
/// The voices the game actually speaks in: one girl and one boy.
///
/// GDD 8.3 describes six slots, and the service still sends them: `cast.py`
/// pins one of V1 to V6 to each of the ten Characters, and `rules.py` refuses to
/// draw a Classroom where two Pupils share one. That constraint is the service's
/// and is untouched by this. The client folds the six down to two, because two
/// recordings is what exists.
///
/// The cost is real and worth naming. `rules.py` separates voices because Pupils
/// speak aloud one after another, and three girls in one Classroom now sound
/// identical. What still tells them apart is the name box, the sprite, and the
/// Personality. Restoring six is a change to this enum and to `slots.py`, and
/// nothing between them.
/// </summary>
public enum VoiceId
{
    None = 0,
    Girl = 1,
    Boy = 2,
}

/// <summary>
/// The girl/boy rule, and the fold from the service's six slots onto it.
/// </summary>
public static class VoiceCatalog
{
    /// <summary>Names the baked clips and the voice states use.</summary>
    public const string GirlKey = "girl";

    public const string BoyKey = "boy";

    /// <summary>
    /// The name this voice is filed under, or an empty string for
    /// <see cref="VoiceId.None"/>.
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
    /// Fold a slot name from the wire onto a voice. V1 to V3 are female and V4
    /// to V6 are male, which is the same split <c>cast.py</c> holds as
    /// <c>FEMALE_VOICES</c> and <c>MALE_VOICES</c>.
    ///
    /// Nothing calls this yet. It is here because the live path needs it: a Pupil
    /// in a real Encounter arrives as <c>ClassroomView.pupils[].voice</c>, and
    /// that field carries a slot, not a voice.
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
                return false;
        }
    }
}

/// <summary>
/// Anything a dialogue line can name as its speaker and that owns a voice.
/// </summary>
public interface IVoicedSpeaker
{
    VoiceId Voice { get; }
}

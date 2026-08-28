using System.Globalization;
using System.Text;

/// <summary>
/// The name a baked clip is filed under: the voice plus a fingerprint of the text.
///
/// Keying on the text rather than on a conversation id and a line number means
/// reordering a conversation keeps its audio and editing a line loses it, which
/// is the behaviour you want in both cases. It also makes the key stable across
/// the repository boundary, so the tool that bakes a clip and the client that
/// plays it agree without sharing a manifest of line numbers.
///
/// <c>Tools/voicelab/voicelab/keys.py</c> is the other half of this and has to
/// stay identical. Its test compares both against the same fixed strings.
/// </summary>
public static class VoiceKey
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>
    /// The key for one spoken line, or an empty string when there is nothing to
    /// play: no voice assigned, or no text.
    /// </summary>
    public static string For(VoiceId voice, string text)
    {
        if (voice == VoiceId.None || string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Concat(VoiceCatalog.ToKey(voice), "_", Fingerprint(Normalise(text)));
    }

    /// <summary>
    /// Collapse every run of whitespace to one space and trim the ends.
    ///
    /// Unity's TextArea wraps long lines when it serialises them, so the same
    /// sentence can reach this method with a newline in it on one machine and a
    /// space on another. Normalising first keeps the fingerprint from changing
    /// when nobody edited the words.
    /// </summary>
    public static string Normalise(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// FNV-1a over the UTF-8 bytes, 64 bits, lower-case hex.
    ///
    /// A cryptographic digest would do just as well and costs a dependency that
    /// has to behave identically under IL2CPP, in the editor, and in CPython.
    /// Sixteen hex characters collide about once in four billion for a cast this
    /// size, which is far below the rate at which someone mistypes a line.
    /// </summary>
    public static string Fingerprint(string normalisedText)
    {
        ulong hash = FnvOffsetBasis;
        byte[] bytes = Encoding.UTF8.GetBytes(normalisedText ?? string.Empty);
        for (int index = 0; index < bytes.Length; index++)
        {
            hash ^= bytes[index];
            hash *= FnvPrime;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}

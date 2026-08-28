using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every baked line, filed by <see cref="VoiceKey"/>.
///
/// Direct asset references rather than a runtime folder scan, so the clips a
/// build needs are the clips a build ships. Rebuild this from the imported audio
/// with <c>Flee the Faculty > Voices > Rebuild Voice Library</c>; nothing here
/// is meant to be filled in by hand.
///
/// This holds authored lines only. A Pupil's real dialogue is written by the
/// model at run time and arrives from the service already spoken, so a miss here
/// is normal and silent rather than an error.
/// </summary>
[CreateAssetMenu(fileName = "Voice Clip Library", menuName = "Dialogue/Voice Clip Library")]
public sealed class VoiceClipLibrary : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [Tooltip("VoiceKey.For(voice, text). Matches the baked file name.")]
        public string key;

        public VoiceId voice;
        public AudioClip clip;

        [Tooltip("The line this was baked from. Reference only; the key is what matches.")]
        [TextArea(1, 4)]
        public string text;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    private Dictionary<string, AudioClip> clipsByKey;

    public IReadOnlyList<Entry> Entries => entries;

    private void OnEnable()
    {
        clipsByKey = null;
    }

    private void OnValidate()
    {
        clipsByKey = null;
    }

    /// <summary>The clip for a line, or null when it has not been baked.</summary>
    public AudioClip Find(VoiceId voice, string text)
    {
        string key = VoiceKey.For(voice, text);
        return string.IsNullOrEmpty(key) ? null : FindByKey(key);
    }

    public AudioClip FindByKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        BuildIndex();
        return clipsByKey.TryGetValue(key, out AudioClip clip) ? clip : null;
    }

    /// <summary>Replace the whole table. Used by the editor rebuild only.</summary>
    public void SetEntries(IEnumerable<Entry> replacement)
    {
        entries = replacement == null ? new List<Entry>() : new List<Entry>(replacement);
        clipsByKey = null;
    }

    private void BuildIndex()
    {
        if (clipsByKey != null)
        {
            return;
        }

        clipsByKey = new Dictionary<string, AudioClip>(entries.Count, StringComparer.Ordinal);
        int broken = 0;
        for (int index = 0; index < entries.Count; index++)
        {
            Entry entry = entries[index];
            if (entry == null || string.IsNullOrEmpty(entry.key))
            {
                continue;
            }

            if (entry.clip == null)
            {
                broken++;
                continue;
            }

            clipsByKey[entry.key] = entry.clip;
        }

        // An entry whose clip failed to resolve is the one failure that looks
        // like success: the library loads, the lookup misses, and the game plays
        // silently. Rebuilding relinks every entry from the folder.
        if (broken > 0)
        {
            Debug.LogError(
                $"{broken} of {entries.Count} voice entries point at a clip that is "
                + "missing. Run Flee the Faculty > Voices > Rebuild Voice Library.",
                this);
        }
    }
}

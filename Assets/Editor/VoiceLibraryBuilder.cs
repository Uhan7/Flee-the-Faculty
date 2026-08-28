using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turn the folder of baked clips into the asset the game loads.
///
/// The second half of the round trip started by <see cref="VoiceLineExporter"/>.
/// Baked files are named after their key, so this needs no manifest to match
/// them up; it reads the exported line list only to fill in the readable text
/// and to say which lines are still missing audio.
///
/// The library lands in Resources because the classroom scene has no saved
/// Dialogue System object to hold a reference, so the player loads it by name
/// when it bootstraps itself.
/// </summary>
public static class VoiceLibraryBuilder
{
    public const string ClipFolder = "Assets/Audio/Voices";
    public const string LibraryAssetPath = "Assets/Resources/Voice Clip Library.asset";

    private const string LineListRelativePath = "Tools/voicelab/lines-to-bake.json";

    /// <summary>
    /// The same vectors as <c>GOLDEN</c> in <c>voicelab/keys.py</c>.
    ///
    /// A disagreement between the two key implementations has no symptom worth
    /// noticing: every clip bakes, every lookup misses, and the game plays
    /// silently. Checking here costs four hashes and catches it at the one
    /// moment somebody is looking.
    /// </summary>
    private static readonly (VoiceId Voice, string Text, string Key)[] KeyVectors =
    {
        (VoiceId.Girl, "Hello there.", "girl_892362dd056bbf55"),
        (VoiceId.Girl, "Plants eat the dirt, right?", "girl_365fe4b4b1fe52f8"),
        (VoiceId.Boy, "  spaced   out\n\nline  ", "boy_5473212866159303"),
        (VoiceId.Girl, "Caf\u00e9 na\u00efve r\u00e9sum\u00e9 \u2014 unicode.", "girl_c522c547d1bd00df"),
    };

    [MenuItem("Flee the Faculty/Voices/Rebuild Voice Library", priority = 101)]
    public static void Rebuild()
    {
        if (!KeysAgreeWithTool())
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder(ClipFolder))
        {
            EditorUtility.DisplayDialog(
                "No baked clips",
                $"{ClipFolder} does not exist yet.\n\n"
                + "Export the lines, run 'uv run voicelab bake-lines' in "
                + "Tools/voicelab, then try again.",
                "OK");
            return;
        }

        Dictionary<string, string> textByKey = LoadExportedText();
        List<VoiceClipLibrary.Entry> entries = new List<VoiceClipLibrary.Entry>();
        List<string> unparsed = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { ClipFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                continue;
            }

            string key = Path.GetFileNameWithoutExtension(path);
            if (!TryReadVoice(key, out VoiceId voice))
            {
                unparsed.Add(Path.GetFileName(path));
                continue;
            }

            entries.Add(new VoiceClipLibrary.Entry
            {
                key = key,
                voice = voice,
                clip = clip,
                text = textByKey.TryGetValue(key, out string text) ? text : string.Empty,
            });
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.key, right.key));

        VoiceClipLibrary library = LoadOrCreateLibrary();
        library.SetEntries(entries);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        int missing = CountMissing(textByKey, entries);
        Debug.Log(
            $"Voice library rebuilt: {entries.Count} clips"
            + (missing > 0 ? $", {missing} exported lines still unbaked" : string.Empty)
            + (unparsed.Count > 0 ? $", {unparsed.Count} files skipped" : string.Empty),
            library);

        foreach (string name in unparsed)
        {
            Debug.LogWarning(
                $"Skipped '{name}': a baked clip must be named <voice>_<fingerprint>, "
                + "for example girl_1a2b3c4d5e6f7a8b.wav.");
        }

        Selection.activeObject = library;
    }

    private static bool KeysAgreeWithTool()
    {
        foreach ((VoiceId voice, string text, string expected) in KeyVectors)
        {
            string actual = VoiceKey.For(voice, text);
            if (actual == expected)
            {
                continue;
            }

            Debug.LogError(
                $"VoiceKey has drifted from voicelab/keys.py: {voice} gives {actual}, "
                + $"expected {expected}. Baked clips would never be found. "
                + "Fix both sides before rebuilding.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The voice name at the front of a key. The rest is the text fingerprint,
    /// which this side never has to recompute.
    /// </summary>
    private static bool TryReadVoice(string key, out VoiceId voice)
    {
        voice = VoiceId.None;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        int separator = key.IndexOf('_');
        return separator > 0 && VoiceCatalog.TryParseKey(key.Substring(0, separator), out voice);
    }

    private static VoiceClipLibrary LoadOrCreateLibrary()
    {
        VoiceClipLibrary existing = AssetDatabase.LoadAssetAtPath<VoiceClipLibrary>(LibraryAssetPath);
        if (existing != null)
        {
            return existing;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        VoiceClipLibrary created = ScriptableObject.CreateInstance<VoiceClipLibrary>();
        AssetDatabase.CreateAsset(created, LibraryAssetPath);
        return created;
    }

    private static Dictionary<string, string> LoadExportedText()
    {
        Dictionary<string, string> byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        string path = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", LineListRelativePath));
        if (!File.Exists(path))
        {
            return byKey;
        }

        LineList parsed = JsonUtility.FromJson<LineList>(File.ReadAllText(path));
        if (parsed?.lines == null)
        {
            return byKey;
        }

        foreach (ExportedLine line in parsed.lines)
        {
            if (line != null && !string.IsNullOrEmpty(line.key))
            {
                byKey[line.key] = line.text;
            }
        }

        return byKey;
    }

    private static int CountMissing(
        Dictionary<string, string> exported, List<VoiceClipLibrary.Entry> entries)
    {
        if (exported.Count == 0)
        {
            return 0;
        }

        HashSet<string> baked = new HashSet<string>(StringComparer.Ordinal);
        foreach (VoiceClipLibrary.Entry entry in entries)
        {
            baked.Add(entry.key);
        }

        int missing = 0;
        foreach (string key in exported.Keys)
        {
            if (!baked.Contains(key))
            {
                missing++;
            }
        }

        return missing;
    }

    [Serializable]
    private sealed class LineList
    {
        public ExportedLine[] lines;
    }

    [Serializable]
    private sealed class ExportedLine
    {
        public string key;
        public string voice;
        public string text;
        public string source;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Collect every authored line that has a voice, and write the list the baking
/// tool reads.
///
/// This is the first half of a round trip. Unity knows which lines exist and who
/// says them; <c>Tools/voicelab</c> knows how to make a slot sound like a slot.
/// Neither can see into the other, so the list of work passes between them as a
/// file, keyed the same way on both sides by <see cref="VoiceKey"/>.
///
/// Only authored dialogue appears here. What a Pupil says in a real Encounter is
/// written by the model at run time and cannot be baked in advance, which is why
/// ADR-0011 has the service speak those lines instead.
/// </summary>
public static class VoiceLineExporter
{
    private const string OutputRelativePath = "Tools/voicelab/lines-to-bake.json";

    /// <summary>
    /// Checked on disk rather than through the AssetDatabase, so a clip that has
    /// just been baked counts even before Unity has imported it.
    /// </summary>
    private static bool HasClip(string key)
    {
        return File.Exists(Path.Combine(
            Application.dataPath, "Audio", "Voices", key + ".wav"));
    }

    [MenuItem("Flee the Faculty/Voices/Export Dialogue Lines", priority = 100)]
    public static void Export()
    {
        List<Line> collected = Collect();
        if (collected.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "No lines to bake",
                "No dialogue line has a voice assigned yet. Set the Voice field on a "
                + "Dialogue Actor or a Dialogue Speaker first.",
                "OK");
            return;
        }

        string path = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", OutputRelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, Serialise(collected));

        List<Line> unbaked = collected.FindAll(line => !HasClip(line.Key));
        Debug.Log(
            $"{collected.Count} voiced lines -> {OutputRelativePath}"
            + (unbaked.Count > 0 ? $", {unbaked.Count} with no clip yet" : ", all baked"));

        foreach (Line line in unbaked)
        {
            Debug.LogWarning($"No clip for {line.Key}: \"{line.Text}\"");
        }

        EditorUtility.DisplayDialog(
            "Lines exported",
            $"{collected.Count} voiced lines written to {OutputRelativePath}.\n\n"
            + (unbaked.Count == 0
                ? "Every line already has a clip. Nothing else to do."
                : $"{unbaked.Count} have no clip and will fall back to voice ticks.\n\n"
                  + "If you hold the voice recordings, in Tools/voicelab:\n"
                  + "  uv run voicelab bake-lines\n"
                  + "then Flee the Faculty > Voices > Rebuild Voice Library.\n\n"
                  + "If you do not, commit lines-to-bake.json and ask whoever does. "
                  + "The Console lists which lines are missing."),
            "OK");
    }

    private sealed class Line
    {
        public string Key;
        public VoiceId Voice;
        public string Text;
        public string Source;
    }

    /// <summary>
    /// Every conversation asset, every prefab that carries one, and every open
    /// scene. Deduplicated by key, because two Characters on the same slot
    /// saying the same words produce one clip and not two.
    /// </summary>
    private static List<Line> Collect()
    {
        Dictionary<string, Line> byKey = new Dictionary<string, Line>(StringComparer.Ordinal);

        foreach (string guid in AssetDatabase.FindAssets("t:Dialogue"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Dialogue dialogue = AssetDatabase.LoadAssetAtPath<Dialogue>(path);
            if (dialogue != null)
            {
                Take(byKey, dialogue, path);
            }
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                continue;
            }

            foreach (SceneDialogueConversation conversation
                in prefab.GetComponentsInChildren<SceneDialogueConversation>(true))
            {
                Take(byKey, conversation, path);
            }
        }

        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SceneDialogueConversation conversation
                    in root.GetComponentsInChildren<SceneDialogueConversation>(true))
                {
                    Take(byKey, conversation, scene.path);
                }
            }
        }

        List<Line> ordered = new List<Line>(byKey.Values);
        ordered.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        return ordered;
    }

    private static void Take(
        Dictionary<string, Line> byKey, IDialogueSequence sequence, string source)
    {
        if (sequence == null || !sequence.HasLines)
        {
            return;
        }

        IReadOnlyList<IDialogueLine> lines = sequence.Lines;
        for (int index = 0; index < lines.Count; index++)
        {
            IDialogueLine line = lines[index];
            if (line == null)
            {
                continue;
            }

            VoiceId voice = VoiceCatalog.VoiceOf(line.SpeakerReference);
            string key = VoiceKey.For(voice, line.Text);
            if (string.IsNullOrEmpty(key) || byKey.ContainsKey(key))
            {
                continue;
            }

            byKey[key] = new Line
            {
                Key = key,
                Voice = voice,
                Text = VoiceKey.Normalise(line.Text),
                Source = source,
            };
        }
    }

    /// <summary>
    /// Hand-rolled because JsonUtility cannot write a plain array of objects and
    /// the file is read by Python, which does not care about Unity's shape rules.
    /// </summary>
    private static string Serialise(List<Line> lines)
    {
        StringBuilder json = new StringBuilder();
        json.Append("{\n  \"lines\": [\n");
        for (int index = 0; index < lines.Count; index++)
        {
            Line line = lines[index];
            json.Append("    {");
            json.Append($"\"key\": {Quote(line.Key)}, ");
            json.Append($"\"voice\": {Quote(VoiceCatalog.ToKey(line.Voice))}, ");
            json.Append($"\"text\": {Quote(line.Text)}, ");
            json.Append($"\"source\": {Quote(line.Source)}");
            json.Append(index == lines.Count - 1 ? "}\n" : "},\n");
        }

        json.Append("  ]\n}\n");
        return json.ToString();
    }

    private static string Quote(string value)
    {
        StringBuilder quoted = new StringBuilder(value.Length + 2);
        quoted.Append('"');
        foreach (char character in value ?? string.Empty)
        {
            switch (character)
            {
                case '"': quoted.Append("\\\""); break;
                case '\\': quoted.Append("\\\\"); break;
                case '\n': quoted.Append("\\n"); break;
                case '\r': quoted.Append("\\r"); break;
                case '\t': quoted.Append("\\t"); break;
                default:
                    if (character < ' ')
                    {
                        quoted.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        quoted.Append(character);
                    }

                    break;
            }
        }

        quoted.Append('"');
        return quoted.ToString();
    }
}

using System.Collections;
using UnityEngine;

/// <summary>
/// Warms generated speech while the transition overlay still covers the scene.
/// Authored clips are local and are skipped.
/// </summary>
public static class DialogueVoicePreloader
{
    private const float ServiceWarmupTimeoutSeconds = 30f;
    private const float GeneratedSpeechTimeoutSeconds = 30f;

    public static IEnumerator Preload(IDialogueSequence sequence)
    {
        DialogueManager manager = DialogueManager.GetOrCreate();
        if (manager == null || !manager.UseTextToSpeech || sequence == null || !sequence.HasLines)
        {
            yield break;
        }

        ServiceVoiceSynthesizer synthesizer = ServiceVoiceSynthesizer.GetOrCreate();
        DialogueVoicePlayer voicePlayer = DialogueVoicePlayer.GetOrCreate();
        if (synthesizer == null || voicePlayer == null)
        {
            yield break;
        }

        float warmupDeadline = Time.unscaledTime + ServiceWarmupTimeoutSeconds;
        while (!synthesizer.IsReady && Time.unscaledTime < warmupDeadline)
        {
            yield return null;
        }

        if (!synthesizer.IsReady)
        {
            yield break;
        }

        for (int index = 0; index < sequence.Lines.Count; index++)
        {
            IDialogueLine line = sequence.Lines[index];
            if (line == null || string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            VoiceId voice = VoiceCatalog.VoiceOf(line.SpeakerReference);
            if (voice == VoiceId.None || voicePlayer.TryGetLineDuration(line, out _))
            {
                continue;
            }

            yield return synthesizer.Speak(
                VoiceCatalog.SlotOf(line.SpeakerReference),
                voice,
                line.Text,
                GeneratedSpeechTimeoutSeconds,
                _ => { });
        }
    }
}

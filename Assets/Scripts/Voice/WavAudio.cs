using UnityEngine;

/// <summary>
/// Turn the WAV the service returns into a playable clip.
///
/// Parsing forty-four bytes rather than calling
/// <c>UnityWebRequestMultimedia.GetAudioClip</c>, and the reason is WebGL: a
/// clip built from a file there stays unloaded and plays silence, whatever the
/// format. Copying samples into a clip this side owns is the path that works. It
/// is also the path the WebAssembly runtime used before ADR-0013, so this is the
/// same mechanism reached by a different route rather than a new risk.
///
/// The parsing itself is in <see cref="WavDecoder"/>, which has no Unity types
/// so that it can be tested outside the editor. This is the glue.
/// </summary>
public static class WavAudio
{
    /// <summary>
    /// Read one spoken line into a clip, or return null when it cannot be read.
    ///
    /// The clip's rate is the file's rate and is not corrected to anything: it
    /// is how a voice slot's pitch shift is carried. See <see cref="WavDecoder"/>.
    /// </summary>
    public static AudioClip ToClip(byte[] wav, string name)
    {
        WavDecoder.Pcm pcm = WavDecoder.Decode(wav);
        if (pcm.IsEmpty)
        {
            return null;
        }

        AudioClip clip = AudioClip.Create(name, pcm.Frames, pcm.Channels, pcm.SampleRate, false);
        clip.SetData(pcm.Samples, 0);
        return clip;
    }
}

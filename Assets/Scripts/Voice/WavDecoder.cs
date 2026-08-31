using System;

/// <summary>
/// Read a WAV file into samples. No Unity types, on purpose.
///
/// The service returns each spoken line as a WAV
/// (<c>POST /v1/speech</c>, ADR-0013), and this is the half of reading one that
/// is pure format parsing. <see cref="WavAudio"/> is the other half, four lines
/// of turning the result into an <c>AudioClip</c>.
///
/// The split is what makes this testable. <c>Tools/wav-decoder-test</c> compiles
/// this one file with <c>csc</c> and runs it against WAVs the service actually
/// produced, which needs no Unity and no editor.
///
/// <h3>The sample rate is not decoration</h3>
///
/// The service renders each of the six voice slots at its own rate, between
/// about 23kHz and 33kHz, because that is how a slot's pitch shift is carried:
/// what arrives is the resampled signal written without resampling it. Playing
/// it at the rate in the header is what makes a slot sound like that slot.
/// Nothing here may substitute a standard rate for it, and nothing here
/// resamples. See <c>speech/slots.py</c> in the service repository.
/// </summary>
public static class WavDecoder
{
    private const int MinimumHeaderBytes = 44;

    /// <summary>One decoded file: interleaved samples in [-1, 1], and its format.</summary>
    public readonly struct Pcm
    {
        public readonly float[] Samples;
        public readonly int Channels;
        public readonly int SampleRate;

        public Pcm(float[] samples, int channels, int sampleRate)
        {
            Samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
        }

        /// <summary>Samples per channel, which is what an AudioClip's length is.</summary>
        public int Frames => Channels > 0 ? Samples.Length / Channels : 0;

        public bool IsEmpty => Samples == null || Samples.Length == 0;
    }

    /// <summary>
    /// Decode 16-bit PCM, or return an empty <see cref="Pcm"/>.
    ///
    /// Empty rather than an exception on every malformed input, because the one
    /// caller treats a missing clip as "no voice for this line" and lets the
    /// syllable ticks carry it. A thrown exception would turn a quiet line into
    /// a stopped coroutine, which is a worse failure than silence.
    /// </summary>
    public static Pcm Decode(byte[] wav)
    {
        if (wav == null || wav.Length < MinimumHeaderBytes)
        {
            return default;
        }

        if (FourCc(wav, 0) != "RIFF" || FourCc(wav, 8) != "WAVE")
        {
            return default;
        }

        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        int dataOffset = -1;
        int dataLength = 0;

        // Walk the chunks rather than assuming the canonical 44-byte layout.
        // Python's `wave` writes exactly that layout today, and a writer that
        // adds a LIST or a fact chunk is perfectly normal and would silently
        // shift every sample by the length of it.
        int position = 12;
        while (position + 8 <= wav.Length)
        {
            string chunkId = FourCc(wav, position);
            int chunkSize = BitConverter.ToInt32(wav, position + 4);
            int chunkStart = position + 8;
            if (chunkSize < 0 || chunkStart + chunkSize > wav.Length)
            {
                // A truncated response. Take what is actually here rather than
                // what the header promised, so a dropped connection costs the
                // end of one line instead of the whole clip.
                chunkSize = wav.Length - chunkStart;
            }

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                channels = BitConverter.ToInt16(wav, chunkStart + 2);
                sampleRate = BitConverter.ToInt32(wav, chunkStart + 4);
                bitsPerSample = BitConverter.ToInt16(wav, chunkStart + 14);
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkStart;
                dataLength = chunkSize;
            }

            // Chunks are word-aligned, so an odd size is followed by a pad byte.
            position = chunkStart + chunkSize + (chunkSize & 1);
        }

        if (dataOffset < 0 || channels <= 0 || sampleRate <= 0 || bitsPerSample != 16)
        {
            return default;
        }

        // Round down to a whole frame. A half-written final frame would otherwise
        // put the channels out of step for the rest of the clip.
        int count = dataLength / 2 / channels * channels;
        if (count <= 0)
        {
            return default;
        }

        // Read the two bytes inline rather than through BitConverter, and scale
        // by a reciprocal rather than dividing. Measured on a 179,712-sample
        // line: 0.60ms with BitConverter, 0.39ms this way, and the obvious
        // alternative of Buffer.BlockCopy into a short[] came out slower at
        // 0.46ms and allocated half a megabyte more per line, which is the
        // wrong trade in WebGL. None of it is a bottleneck at four lines a
        // minute; it is simply the cheapest of three ways to write the same
        // loop, and `DecoderTests` checks it against the plain one.
        float[] samples = new float[count];
        const float scale = 1f / 32768f;
        for (int index = 0, at = dataOffset; index < count; index++, at += 2)
        {
            samples[index] = (short)(wav[at] | (wav[at + 1] << 8)) * scale;
        }

        return new Pcm(samples, channels, sampleRate);
    }

    private static string FourCc(byte[] bytes, int offset)
    {
        if (offset + 4 > bytes.Length)
        {
            return string.Empty;
        }

        return new string(new[]
        {
            (char)bytes[offset],
            (char)bytes[offset + 1],
            (char)bytes[offset + 2],
            (char)bytes[offset + 3],
        });
    }
}

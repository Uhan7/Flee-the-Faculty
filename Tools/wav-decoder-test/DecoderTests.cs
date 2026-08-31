// Exercises the real WavDecoder.cs against WAVs the service actually produced.
//
//     ./Tools/wav-decoder-test/run.sh
//
// No Unity and no editor: `csc` compiles the shipped decoder with this file and
// mono runs it. That is possible because WavDecoder.cs has no Unity types in it,
// which is the whole reason it is a separate file from WavAudio.cs.
//
// What is under test is the one thing about ADR-0013 that is easy to get subtly
// wrong and impossible to hear. A voice slot's pitch is carried by the file's
// sample rate rather than by resampling, so a decoder that helpfully substitutes
// 22050Hz, or that reads the rate from the wrong offset, produces audio that
// plays and is the wrong Pupil. Everything else here is the ordinary parsing:
// odd chunk sizes, chunks before `data`, and a response that was cut off.

using System;
using System.Collections.Generic;
using System.IO;

public static class DecoderTests
{
    private static int failures;

    public static int Main(string[] args)
    {
        Check("a canonical file round-trips", CanonicalRoundTrip);
        Check("the slot's sample rate survives", SampleRateSurvives);
        Check("a chunk before data is skipped", ExtraChunkSkipped);
        Check("an odd-sized chunk is padded", OddChunkPadded);
        Check("a truncated body keeps what arrived", TruncatedBody);
        Check("stereo frames are counted per channel", StereoFrames);
        Check("garbage is empty rather than an exception", GarbageIsEmpty);
        Check("the fast inner loop matches the plain one", MatchesPlainRead);

        string sampleDir = args.Length > 0 ? args[0] : null;
        if (sampleDir != null && Directory.Exists(sampleDir))
        {
            foreach (string path in Directory.GetFiles(sampleDir, "*.wav"))
            {
                Check($"real service output: {Path.GetFileName(path)}",
                    () => RealFile(path));
            }
        }
        else if (sampleDir != null)
        {
            Console.WriteLine($"  (no sample directory at {sampleDir}, skipping real files)");
        }

        Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Check(string name, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            failures++;
            Console.WriteLine($"  FAIL {name}: {exception.Message}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    // ---- The cases ----

    private static void CanonicalRoundTrip()
    {
        short[] samples = { 0, 16384, -16384, 32767, -32768 };
        WavDecoder.Pcm pcm = WavDecoder.Decode(Wav(samples, 1, 22050));

        Assert(!pcm.IsEmpty, "decoded nothing");
        Assert(pcm.Channels == 1, $"channels {pcm.Channels}");
        Assert(pcm.SampleRate == 22050, $"rate {pcm.SampleRate}");
        Assert(pcm.Frames == samples.Length, $"frames {pcm.Frames}");
        Assert(Math.Abs(pcm.Samples[1] - 0.5f) < 1e-6, $"sample {pcm.Samples[1]}");
        Assert(Math.Abs(pcm.Samples[4] + 1.0f) < 1e-6, $"sample {pcm.Samples[4]}");
    }

    private static void SampleRateSurvives()
    {
        // The six slots, as speech/slots.py renders them at a 22050Hz base.
        foreach (int rate in new[] { 33184, 33184, 27904, 31275, 23430, 26299 })
        {
            WavDecoder.Pcm pcm = WavDecoder.Decode(Wav(new short[] { 1, 2, 3 }, 1, rate));
            Assert(pcm.SampleRate == rate, $"asked for {rate}, decoded {pcm.SampleRate}");
        }
    }

    private static void ExtraChunkSkipped()
    {
        byte[] wav = Wav(new short[] { 100, 200 }, 1, 27904, extraChunk: "LIST", extraBytes: 8);
        WavDecoder.Pcm pcm = WavDecoder.Decode(wav);
        Assert(pcm.Frames == 2, $"frames {pcm.Frames}");
        Assert(Math.Abs(pcm.Samples[0] - 100f / 32768f) < 1e-6, "samples shifted by the extra chunk");
    }

    private static void OddChunkPadded()
    {
        byte[] wav = Wav(new short[] { 100, 200 }, 1, 27904, extraChunk: "LIST", extraBytes: 5);
        WavDecoder.Pcm pcm = WavDecoder.Decode(wav);
        Assert(pcm.Frames == 2, $"frames {pcm.Frames}");
        Assert(Math.Abs(pcm.Samples[1] - 200f / 32768f) < 1e-6, "samples shifted by the pad byte");
    }

    private static void TruncatedBody()
    {
        byte[] full = Wav(new short[] { 10, 20, 30, 40, 50, 60 }, 1, 22050);
        byte[] cut = new byte[full.Length - 6];
        Array.Copy(full, cut, cut.Length);

        WavDecoder.Pcm pcm = WavDecoder.Decode(cut);
        Assert(pcm.Frames == 3, $"frames {pcm.Frames}, expected the three that arrived");
    }

    private static void StereoFrames()
    {
        WavDecoder.Pcm pcm = WavDecoder.Decode(Wav(new short[] { 1, 2, 3, 4 }, 2, 22050));
        Assert(pcm.Channels == 2, $"channels {pcm.Channels}");
        Assert(pcm.Frames == 2, $"frames {pcm.Frames}");
    }

    private static void GarbageIsEmpty()
    {
        Assert(WavDecoder.Decode(null).IsEmpty, "null decoded to something");
        Assert(WavDecoder.Decode(new byte[0]).IsEmpty, "empty decoded to something");
        Assert(WavDecoder.Decode(new byte[64]).IsEmpty, "zeroes decoded to something");

        byte[] wrongMagic = Wav(new short[] { 1 }, 1, 22050);
        wrongMagic[0] = (byte)'X';
        Assert(WavDecoder.Decode(wrongMagic).IsEmpty, "a bad RIFF header decoded");
    }

    private static void MatchesPlainRead()
    {
        // Decode reads each sample's two bytes inline and scales by a
        // reciprocal, because it is the cheapest of three ways to write that
        // loop. This is the plain way, kept as the thing it has to agree with.
        var samples = new short[4096];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(i * 977 - 32768);
        }

        WavDecoder.Pcm pcm = WavDecoder.Decode(Wav(samples, 1, 22050));
        Assert(pcm.Samples.Length == samples.Length, $"length {pcm.Samples.Length}");
        for (int i = 0; i < samples.Length; i++)
        {
            float plain = BitConverter.ToInt16(BitConverter.GetBytes(samples[i]), 0) / 32768f;
            Assert(pcm.Samples[i] == plain, $"sample {i}: {pcm.Samples[i]} against {plain}");
        }
    }

    private static void RealFile(string path)
    {
        WavDecoder.Pcm pcm = WavDecoder.Decode(File.ReadAllBytes(path));
        Assert(!pcm.IsEmpty, "decoded nothing");
        Assert(pcm.Channels == 1, $"channels {pcm.Channels}, the service sends mono");
        Assert(pcm.SampleRate >= 8000 && pcm.SampleRate <= 96000,
            $"rate {pcm.SampleRate} is outside what a browser will play");
        Assert(pcm.Frames > 0, "no frames");

        float peak = 0f;
        foreach (float sample in pcm.Samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        // The engine levels every line to -3 dBFS and then applies the slot's
        // gain, the largest of which is +4dB. Silence means the decode landed on
        // the wrong offset; clipping means the levelling is wrong.
        Assert(peak > 0.1f, $"peak {peak:0.000}, this is silence");
        Assert(peak <= 1.0f, $"peak {peak:0.000}, this clips");
        Console.Write($"       {pcm.Frames} frames at {pcm.SampleRate}Hz, peak {peak:0.00}  ");
    }

    // ---- A WAV writer, so the cases above do not need the service ----

    private static byte[] Wav(
        short[] samples, int channels, int sampleRate,
        string extraChunk = null, int extraBytes = 0)
    {
        List<byte> body = new List<byte>();

        void FourCc(string value)
        {
            foreach (char character in value)
            {
                body.Add((byte)character);
            }
        }

        FourCc("fmt ");
        body.AddRange(BitConverter.GetBytes(16));
        body.AddRange(BitConverter.GetBytes((short)1));
        body.AddRange(BitConverter.GetBytes((short)channels));
        body.AddRange(BitConverter.GetBytes(sampleRate));
        body.AddRange(BitConverter.GetBytes(sampleRate * channels * 2));
        body.AddRange(BitConverter.GetBytes((short)(channels * 2)));
        body.AddRange(BitConverter.GetBytes((short)16));

        if (extraChunk != null)
        {
            FourCc(extraChunk);
            body.AddRange(BitConverter.GetBytes(extraBytes));
            body.AddRange(new byte[extraBytes]);
            if ((extraBytes & 1) == 1)
            {
                body.Add(0);
            }
        }

        FourCc("data");
        body.AddRange(BitConverter.GetBytes(samples.Length * 2));
        foreach (short sample in samples)
        {
            body.AddRange(BitConverter.GetBytes(sample));
        }

        List<byte> file = new List<byte>();
        foreach (char character in "RIFF")
        {
            file.Add((byte)character);
        }

        file.AddRange(BitConverter.GetBytes(4 + body.Count));
        foreach (char character in "WAVE")
        {
            file.Add((byte)character);
        }

        file.AddRange(body);
        return file.ToArray();
    }
}

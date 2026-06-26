namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Supplies a small WAV buffer for the on-device model "Test" action. Prefers a
/// real sample shipped inside the downloaded bundle (the sherpa Parakeet bundle
/// ships <c>test_wavs/*.wav</c>) so a non-empty transcript is a true end-to-end
/// pass; falls back to a synthesized 16 kHz mono tone — which still exercises the
/// native load + decode + inference path — when no sample is present (e.g. the
/// Whisper .bin ships none). <see cref="Audio.WavAudioDecoder"/> resamples any
/// rate, so a synthetic clip is always valid input.
/// </summary>
public static class SelfTestAudio
{
    public sealed record Sample(byte[] Wav, string Source);

    public static Sample Resolve(string? modelDir)
    {
        if (!string.IsNullOrEmpty(modelDir) && Directory.Exists(modelDir))
        {
            var wav = Directory.GetFiles(modelDir, "*.wav", SearchOption.AllDirectories).FirstOrDefault();
            if (wav is not null)
            {
                try { return new Sample(File.ReadAllBytes(wav), "model_sample"); }
                catch (IOException) { /* fall through to synth */ }
            }
        }
        return new Sample(Synthesize(), "synthesized");
    }

    private static byte[] Synthesize()
    {
        const int sampleRate = 16000;
        const int samples = sampleRate / 2; // 0.5 s
        var pcm = new short[samples];
        for (int i = 0; i < samples; i++)
            pcm[i] = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / sampleRate));
        return BuildPcm16Wav(pcm, channels: 1, sampleRate: sampleRate);
    }

    private static byte[] BuildPcm16Wav(short[] interleaved, int channels, int sampleRate)
    {
        int dataLen = interleaved.Length * 2;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);                  // PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 2); // byte rate
        w.Write((short)(channels * 2));     // block align
        w.Write((short)16);                 // bits
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        foreach (var s in interleaved) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}

using RadioPad.Infrastructure.Audio;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Phase 1 activation — unit tests for the in-process <see cref="WavAudioDecoder"/>
/// (the desktop client converts its recording to 16 kHz mono WAV in the browser;
/// the server decodes it with no native binary). Covers PCM16 / float32, stereo
/// down-mix, resampling, and the non-WAV guard. Deterministic, no native deps.
/// </summary>
public class WavAudioDecoderTests
{
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

    private static byte[] BuildFloat32Wav(float[] interleaved, int channels, int sampleRate)
    {
        int dataLen = interleaved.Length * 4;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)3);                  // IEEE float
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 4); // byte rate
        w.Write((short)(channels * 4));     // block align
        w.Write((short)32);                 // bits
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        foreach (var s in interleaved) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Decode_Pcm16_Mono_16k()
    {
        var wav = BuildPcm16Wav(new short[] { 0, 16384, -16384, 32767, -32768 }, 1, 16000);
        var f = WavAudioDecoder.Decode(wav);
        Assert.Equal(5, f.Length);
        Assert.Equal(0f, f[0], 3);
        Assert.Equal(0.5f, f[1], 2);
        Assert.Equal(-0.5f, f[2], 2);
        Assert.True(f[3] > 0.99f);
        Assert.Equal(-1f, f[4], 3);
    }

    [Fact]
    public void Decode_Stereo_Downmixes_To_Mono()
    {
        // frame0: L≈+1, R=-1 -> ~0 ; frame1: L=0.5, R=0.5 -> 0.5
        var wav = BuildPcm16Wav(new short[] { 32767, -32768, 16384, 16384 }, 2, 16000);
        var f = WavAudioDecoder.Decode(wav);
        Assert.Equal(2, f.Length);
        Assert.Equal(0f, f[0], 2);
        Assert.Equal(0.5f, f[1], 2);
    }

    [Fact]
    public void Decode_Float32_Mono()
    {
        var wav = BuildFloat32Wav(new[] { 0f, 0.25f, -0.75f }, 1, 16000);
        var f = WavAudioDecoder.Decode(wav);
        Assert.Equal(3, f.Length);
        Assert.Equal(0.25f, f[1], 3);
        Assert.Equal(-0.75f, f[2], 3);
    }

    [Fact]
    public void Decode_Resamples_8k_To_16k()
    {
        var s = new short[100];
        for (int i = 0; i < s.Length; i++) s[i] = (short)(i * 100);
        var wav = BuildPcm16Wav(s, 1, 8000);
        var f = WavAudioDecoder.Decode(wav);
        Assert.Equal(200, f.Length); // 100 * 16000/8000
    }

    [Fact]
    public async Task DecodeAsync_From_Stream()
    {
        var wav = BuildPcm16Wav(new short[] { 16384 }, 1, 16000);
        var f = await new WavAudioDecoder().DecodeAsync(new MemoryStream(wav), "audio/wav", CancellationToken.None);
        Assert.Single(f);
        Assert.Equal(0.5f, f[0], 2);
    }

    [Fact]
    public void Decode_NonWav_Throws()
    {
        var notWav = new byte[64]; // no RIFF/WAVE
        Assert.Throws<NotSupportedException>(() => { WavAudioDecoder.Decode(notWav); });
    }
}

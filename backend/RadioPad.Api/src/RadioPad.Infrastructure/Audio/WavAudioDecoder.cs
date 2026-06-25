using System.Buffers.Binary;

namespace RadioPad.Infrastructure.Audio;

/// <summary>
/// Phase 1 (local STT) — decodes recorded audio into the 16 kHz mono 32-bit-float
/// PCM that the on-device STT engines require. Runs fully on-device.
/// </summary>
public interface IAudioDecoder
{
    /// <summary>
    /// True when the decoder can actually run. Gating the local engine on this
    /// prevents a misconfigured build from silently breaking dictation.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Decode <paramref name="input"/> to mono 16 kHz <see cref="float"/> PCM in
    /// [-1, 1]. <paramref name="contentType"/> is advisory.
    /// </summary>
    Task<float[]> DecodeAsync(Stream input, string contentType, CancellationToken ct);
}

/// <summary>
/// In-process WAV (RIFF/WAVE) decoder — no native binary, no ffmpeg. The desktop
/// client converts its recording to 16 kHz mono WAV in the browser (the WebView2
/// Chromium engine already ships the Opus codec via <c>decodeAudioData</c>) and
/// posts that, so the server side stays pure-managed and fully unit-testable.
/// Supports PCM uint8 / int16 / int24 / int32 and IEEE float32, any channel count
/// (down-mixed to mono) and any sample rate (linearly resampled to 16 kHz).
/// </summary>
public sealed class WavAudioDecoder : IAudioDecoder
{
    public const int TargetSampleRate = 16000;

    public bool Available => true;

    public async Task<float[]> DecodeAsync(Stream input, string contentType, CancellationToken ct)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms, ct);
        return Decode(ms.ToArray());
    }

    /// <summary>Decode a complete WAV byte buffer to 16 kHz mono float PCM.</summary>
    public static float[] Decode(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 44
            || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F'
            || wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
            throw new NotSupportedException("local STT expects a WAV (RIFF/WAVE) payload");

        int audioFormat = 0, channels = 0, sampleRate = 0, bits = 0;
        int dataOffset = -1, dataLength = 0;

        // Walk the RIFF chunks (word-aligned) to find `fmt ` and `data`.
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = wav.Slice(pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(pos + 4, 4));
            int body = pos + 8;
            if (size < 0 || body + size > wav.Length) size = wav.Length - body;

            if (id[0] == 'f' && id[1] == 'm' && id[2] == 't' && id[3] == ' ' && size >= 16)
            {
                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 14, 2));
                // WAVE_FORMAT_EXTENSIBLE: the real format tag lives in the subformat GUID's first 2 bytes.
                if (audioFormat == 0xFFFE && size >= 26)
                    audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 24, 2));
            }
            else if (id[0] == 'd' && id[1] == 'a' && id[2] == 't' && id[3] == 'a')
            {
                dataOffset = body;
                dataLength = size;
            }

            pos = body + size + (size & 1); // chunks are word (2-byte) aligned
        }

        if (dataOffset < 0 || channels <= 0 || sampleRate <= 0 || bits <= 0)
            throw new NotSupportedException("malformed WAV (missing fmt/data chunk)");

        var data = wav.Slice(dataOffset, dataLength);
        var interleaved = ReadSamples(data, audioFormat, bits);
        var mono = DownmixToMono(interleaved, channels);
        return sampleRate == TargetSampleRate ? mono : Resample(mono, sampleRate, TargetSampleRate);
    }

    /// <summary>Decode raw sample bytes to float [-1,1], interleaved across channels.</summary>
    private static float[] ReadSamples(ReadOnlySpan<byte> data, int audioFormat, int bits)
    {
        // audioFormat: 1 = PCM (int), 3 = IEEE float.
        if (audioFormat == 3 && bits == 32)
        {
            int n = data.Length / 4;
            var outp = new float[n];
            for (int i = 0; i < n; i++)
                outp[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(i * 4, 4)));
            return outp;
        }
        if (audioFormat == 1)
        {
            switch (bits)
            {
                case 16:
                {
                    int n = data.Length / 2;
                    var outp = new float[n];
                    for (int i = 0; i < n; i++)
                        outp[i] = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2, 2)) / 32768f;
                    return outp;
                }
                case 8: // 8-bit PCM is unsigned, centred at 128
                {
                    var outp = new float[data.Length];
                    for (int i = 0; i < data.Length; i++)
                        outp[i] = (data[i] - 128) / 128f;
                    return outp;
                }
                case 24:
                {
                    int n = data.Length / 3;
                    var outp = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int b0 = data[i * 3], b1 = data[i * 3 + 1], b2 = data[i * 3 + 2];
                        int s = b0 | (b1 << 8) | (b2 << 16);
                        if ((s & 0x800000) != 0) s |= unchecked((int)0xFF000000); // sign-extend
                        outp[i] = s / 8388608f;
                    }
                    return outp;
                }
                case 32:
                {
                    int n = data.Length / 4;
                    var outp = new float[n];
                    for (int i = 0; i < n; i++)
                        outp[i] = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(i * 4, 4)) / 2147483648f;
                    return outp;
                }
            }
        }
        throw new NotSupportedException($"unsupported WAV sample format (format={audioFormat}, bits={bits})");
    }

    private static float[] DownmixToMono(float[] interleaved, int channels)
    {
        if (channels == 1) return interleaved;
        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            int b = f * channels;
            for (int c = 0; c < channels; c++) sum += interleaved[b + c];
            mono[f] = sum / channels;
        }
        return mono;
    }

    /// <summary>Linear-interpolation resample (a safety net; the client already targets 16 kHz).</summary>
    private static float[] Resample(float[] src, int srcRate, int dstRate)
    {
        if (src.Length == 0) return src;
        long outLen = (long)src.Length * dstRate / srcRate;
        if (outLen <= 0) return Array.Empty<float>();
        var outp = new float[outLen];
        double ratio = (double)srcRate / dstRate;
        for (int i = 0; i < outLen; i++)
        {
            double srcPos = i * ratio;
            int i0 = (int)srcPos;
            int i1 = Math.Min(i0 + 1, src.Length - 1);
            float frac = (float)(srcPos - i0);
            outp[i] = src[i0] * (1 - frac) + src[i1] * frac;
        }
        return outp;
    }
}

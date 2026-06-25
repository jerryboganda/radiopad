using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RadioPad.Infrastructure.Audio;

/// <summary>
/// Phase 1 (local STT) — decodes an arbitrary recorded-audio container (the
/// dictation overlay records <c>audio/webm</c> Opus; WAV / MP4 / OGG are also
/// accepted by the upload route) into the 16 kHz mono 32-bit-float PCM that the
/// on-device STT engines (sherpa-onnx / Whisper.net) require. Implementations
/// run fully on-device — no audio ever leaves the machine.
/// </summary>
public interface IAudioDecoder
{
    /// <summary>
    /// True when the decoder can actually run (its backing binary is resolvable).
    /// Gating the local engine on this prevents a misconfigured desktop build
    /// from silently breaking dictation — it falls back to the cloud path instead.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Decode <paramref name="input"/> to mono 16 kHz <see cref="float"/> PCM in
    /// [-1, 1]. <paramref name="contentType"/> is advisory; the decoder sniffs the
    /// container itself.
    /// </summary>
    Task<float[]> DecodeAsync(Stream input, string contentType, CancellationToken ct);
}

/// <summary>
/// FFmpeg-backed <see cref="IAudioDecoder"/>. Shells a short-lived
/// <c>ffmpeg</c> process to transcode any input container to raw
/// <c>f32le</c> mono 16 kHz on stdout. The binary is resolved from
/// <c>RADIOPAD_FFMPEG_BIN</c> (an absolute path the desktop bundle sets to the
/// ffmpeg shipped alongside the sidecar) or, failing that, <c>ffmpeg</c> on
/// PATH. Input is staged to a temp file rather than piped to stdin so the
/// process can never deadlock on a full stdout pipe.
/// </summary>
public sealed class FfmpegAudioDecoder : IAudioDecoder
{
    private const int TargetSampleRate = 16000;

    private readonly ILogger<FfmpegAudioDecoder> _log;
    private readonly string _ffmpeg;
    private readonly bool _explicitBinExists;

    public FfmpegAudioDecoder(ILogger<FfmpegAudioDecoder> log)
    {
        _log = log;
        var configured = Environment.GetEnvironmentVariable("RADIOPAD_FFMPEG_BIN");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _ffmpeg = configured.Trim();
            _explicitBinExists = File.Exists(_ffmpeg);
        }
        else
        {
            _ffmpeg = "ffmpeg"; // resolved from PATH
            _explicitBinExists = false;
        }
    }

    /// <summary>
    /// Available when an explicit <c>RADIOPAD_FFMPEG_BIN</c> path exists, or when
    /// no explicit path was configured (then we optimistically rely on PATH — the
    /// first decode surfaces a clear error if <c>ffmpeg</c> is genuinely absent).
    /// </summary>
    public bool Available => _explicitBinExists || _ffmpeg == "ffmpeg";

    public async Task<float[]> DecodeAsync(Stream input, string contentType, CancellationToken ct)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var tmp = Path.Combine(Path.GetTempPath(), $"rp-stt-{Guid.NewGuid():N}.media");
        try
        {
            await using (var fs = File.Create(tmp))
                await input.CopyToAsync(fs, ct);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(tmp);
            psi.ArgumentList.Add("-ac");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add(TargetSampleRate.ToString());
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("f32le");
            psi.ArgumentList.Add("-acodec");
            psi.ArgumentList.Add("pcm_f32le");
            psi.ArgumentList.Add("pipe:1");

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"failed to start ffmpeg ('{_ffmpeg}')");

            using var outBuf = new MemoryStream();
            var copyOut = proc.StandardOutput.BaseStream.CopyToAsync(outBuf, ct);
            var readErr = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            await copyOut;
            var stderr = await readErr;

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"ffmpeg exited {proc.ExitCode} while decoding {contentType}: {stderr.Trim()}");

            var bytes = outBuf.ToArray();
            // f32le == little-endian IEEE-754 single. On x86/x64 (and ARM little-
            // endian, the only targets we ship) a raw block copy is correct.
            var samples = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
            return samples;
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { /* best-effort temp cleanup */ }
        }
    }
}

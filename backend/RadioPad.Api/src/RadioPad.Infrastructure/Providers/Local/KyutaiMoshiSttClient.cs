using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Phase 3 — the 3rd, decorrelated cross-check engine: Kyutai STT 1B (en/fr)
/// converted to GGUF and run on CPU + system RAM via a moshi.cpp subprocess (no
/// VRAM), per the on-device STT rule. Architecturally distinct from the Parakeet
/// transducer and the Whisper attention-decoder, so it makes DIFFERENT errors —
/// exactly what ROVER needs to actually reduce WER on the cross-check.
///
/// Dormant unless the local engine is enabled AND both the moshi.cpp binary
/// (<c>RADIOPAD_STT_MOSHI_BIN</c>) and the GGUF model are present, so it is inert
/// on web/server and until the operator provisions the artifacts — at which point
/// it joins the cross-check automatically with no other change.
/// </summary>
public sealed class KyutaiMoshiSttClient : ILocalSttEngine
{
    public const string EngineName = "kyutai";

    // The moshi.cpp CLI emits text without per-token probabilities; use a single
    // confident prior (it is a strong 1B model). The reconciler calibrates votes.
    private const double TokenConfidence = 0.8;

    private readonly ILogger<KyutaiMoshiSttClient> _log;
    private readonly bool _enabled;
    private volatile bool _loadFailed;
    private volatile string? _lastError;

    public KyutaiMoshiSttClient(ILogger<KyutaiMoshiSttClient> log)
    {
        _log = log;
        _enabled = LocalSttModels.IsEnabled();
    }

    public string EngineId => EngineName;

    public string? LastError => _lastError;

    public bool Available =>
        _enabled
        && !_loadFailed
        && LocalSttModels.ResolveMoshiBin() is not null
        && LocalSttModels.ResolveKyutaiGguf() is not null;

    public async Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct)
    {
        var bin = LocalSttModels.ResolveMoshiBin()
            ?? throw new InvalidOperationException("moshi.cpp binary is not configured");
        var gguf = LocalSttModels.ResolveKyutaiGguf()
            ?? throw new InvalidOperationException("kyutai GGUF model is not present");

        var tmp = Path.Combine(Path.GetTempPath(), $"radiopad-xc-{Guid.NewGuid():N}.wav");
        try
        {
            await File.WriteAllBytesAsync(tmp, wavBytes, ct);
            var args = LocalSttModels.ResolveMoshiArgs()
                .Replace("{model}", Quote(gguf))
                .Replace("{audio}", Quote(tmp));

            var text = await RunProcessAsync(bin, args, ct);
            var tokens = SttText.Tokenize(text, TokenConfidence).ToList();
            return new EngineTranscript(EngineName, tokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loadFailed = true;
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            _log.LogError(ex, "kyutai/moshi recognize failed; disabling engine for this session");
            throw;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort cleanup */ }
        }
    }

    private static string Quote(string p) => p.Contains(' ') ? $"\"{p}\"" : p;

    private static async Task<string> RunProcessAsync(string bin, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = bin,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"moshi.cpp exited {proc.ExitCode}: {Truncate(stderr, 500)}");
        return stdout.Trim();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

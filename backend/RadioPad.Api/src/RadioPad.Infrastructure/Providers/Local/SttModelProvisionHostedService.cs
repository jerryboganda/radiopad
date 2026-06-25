using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Background first-run download of the on-device STT model. Runs ONLY when the
/// desktop build has enabled the local engine (<c>RADIOPAD_LOCAL_STT_ENABLED</c>);
/// on web/server (flag unset) it is an immediate no-op. Non-blocking: dictation
/// transparently uses the cloud path until the model lands, then flips to
/// on-device automatically (the engine resolves the model lazily).
/// </summary>
public sealed class SttModelProvisionHostedService : BackgroundService
{
    private readonly SttModelProvisioner _provisioner;
    private readonly ILogger<SttModelProvisionHostedService> _log;

    public SttModelProvisionHostedService(
        SttModelProvisioner provisioner,
        ILogger<SttModelProvisionHostedService> log)
    {
        _provisioner = provisioner;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!LocalSttModels.IsEnabled())
            return; // web/server, or desktop with the engine disabled

        try
        {
            await _provisioner.EnsureAsync(stoppingToken);
            // Ensemble mode also needs the decorrelated second engine (Whisper).
            if (LocalSttModels.IsEnsembleEnabled())
                await _provisioner.EnsureWhisperAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // host shutting down — nothing to do
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "on-device STT model provisioning failed; dictation falls back to cloud");
        }
    }
}

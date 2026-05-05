using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RadioPad.Infrastructure.Integration;

/// <summary>
/// Iter-31 INT-006 — TCP listener that frames inbound HL7 v2 messages over
/// MLLP and dispatches them to <see cref="Hl7MessageHandler"/>.
///
/// <para>Activation: requires <c>RADIOPAD_HL7_MLLP_PORT</c> to be set. When
/// unset, the listener logs an info-level disabled message and never opens a
/// socket.</para>
///
/// <para>Bind address: <c>RADIOPAD_HL7_MLLP_BIND</c> (default
/// <c>127.0.0.1</c>) — remote exposure must be opted into by the operator,
/// matching the platform-wide local-trust rule.</para>
/// </summary>
public sealed class Hl7MllpListener : BackgroundService
{
    public const string PortEnvVar = "RADIOPAD_HL7_MLLP_PORT";
    public const string BindEnvVar = "RADIOPAD_HL7_MLLP_BIND";

    private readonly Hl7MessageHandler _handler;
    private readonly ILogger<Hl7MllpListener> _log;

    public Hl7MllpListener(Hl7MessageHandler handler, ILogger<Hl7MllpListener> log)
    {
        _handler = handler;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var portStr = Environment.GetEnvironmentVariable(PortEnvVar);
        if (string.IsNullOrWhiteSpace(portStr))
        {
            _log.LogInformation("HL7 MLLP listener disabled (set {EnvVar} to enable).", PortEnvVar);
            return;
        }
        if (!int.TryParse(portStr, out var port) || port <= 0 || port > 65535)
        {
            _log.LogWarning("HL7 MLLP listener disabled — invalid {EnvVar}={Value}.", PortEnvVar, portStr);
            return;
        }

        var bindStr = Environment.GetEnvironmentVariable(BindEnvVar) ?? "127.0.0.1";
        if (!IPAddress.TryParse(bindStr, out var ip))
        {
            _log.LogWarning("HL7 MLLP listener disabled — invalid {EnvVar}={Value}.", BindEnvVar, bindStr);
            return;
        }

        var listener = new TcpListener(ip, port);
        try
        {
            listener.Start();
            _log.LogInformation("HL7 MLLP listener started on {Bind}:{Port}.", bindStr, port);
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "HL7 MLLP accept failed; continuing.");
                    continue;
                }
                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }
        finally
        {
            try { listener.Stop(); } catch { /* best effort */ }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var stream = client.GetStream();
                while (!ct.IsCancellationRequested)
                {
                    var frame = await MllpFramer.ReadFrameAsync(stream, ct);
                    if (frame is null) return;
                    var result = await _handler.HandleAsync(frame, ct);
                    var ackBytes = MllpFramer.Wrap(result.Ack);
                    await stream.WriteAsync(ackBytes, ct);
                    await stream.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "HL7 MLLP client handler error.");
            }
        }
    }
}

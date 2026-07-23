using System.Runtime.CompilerServices;
using System.Text;

namespace RadioPad.Application.Providers;

/// <summary>
/// AI-013 — minimal Server-Sent-Events line reader shared by every streaming AI adapter.
/// It accumulates <c>event:</c> / <c>data:</c> lines, yields <c>(evt, data)</c> on the blank
/// line that terminates an event, and stops when a <c>data: [DONE]</c> sentinel arrives
/// (OpenAI-family terminator; harmless for providers that never send it).
///
/// <para>Lives in <c>RadioPad.Application</c> (which has no Infrastructure reference) so it can
/// be reused by both the Application-layer adapters (Anthropic) and the Infrastructure-layer
/// adapters (OpenAI-family, llama.cpp), since Infrastructure references Application.</para>
///
/// <para>Cancellation propagates: an <see cref="OperationCanceledException"/> thrown by the
/// underlying stream mid-read surfaces to the caller unchanged (adapters must map it to a
/// cancellation, never to a transport error).</para>
/// </summary>
public static class SseStreamReader
{
    public static async IAsyncEnumerable<(string? evt, string data)> ReadAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? evt = null;
        var data = new StringBuilder();

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (line.Length == 0)
            {
                // Blank line terminates the current event.
                if (data.Length > 0)
                {
                    var payload = data.ToString();
                    if (payload == "[DONE]") yield break;
                    yield return (evt, payload);
                }
                evt = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith(':')) continue; // SSE comment / keep-alive

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                evt = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var chunk = line["data:".Length..];
                if (chunk.StartsWith(' ')) chunk = chunk[1..]; // strip the single optional leading space
                if (data.Length > 0) data.Append('\n');
                data.Append(chunk);
            }
        }

        // Flush a trailing event that arrived without a terminating blank line.
        if (data.Length > 0)
        {
            var payload = data.ToString();
            if (payload != "[DONE]") yield return (evt, payload);
        }
    }
}

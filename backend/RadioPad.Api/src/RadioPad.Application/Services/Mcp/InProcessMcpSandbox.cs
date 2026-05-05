using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using RadioPad.Application.Abstractions;

namespace RadioPad.Application.Services.Mcp;

/// <summary>
/// Iter-31 MCP-006 — minimal in-process sandbox. The v0.1 sandbox supports
/// only pure JSON-in / JSON-out delegates registered through
/// <see cref="Register"/>; the registered body MUST NOT touch the file
/// system or network. There is no hard OS-level isolation here — that's the
/// next iteration's job (likely WASM via Wasmtime). The single guarantee
/// this layer provides is the wall-clock timeout via a linked
/// <see cref="CancellationTokenSource"/> and the connector-allowlist regex
/// gate on outbound HTTP calls made via <see cref="CheckConnector"/>.
/// </summary>
public sealed class InProcessMcpSandbox : IMcpSandbox
{
    private readonly ConcurrentDictionary<string, Func<string, CancellationToken, Task<string>>> _tools
        = new(StringComparer.Ordinal);

    public IMcpSandbox Register(string toolName, Func<string, CancellationToken, Task<string>> body)
    {
        _tools[toolName] = body;
        return this;
    }

    public async Task<string> InvokeAsync(
        string toolName,
        string inputJson,
        IReadOnlyList<string> allowedConnectorPatterns,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (!_tools.TryGetValue(toolName, out var body))
            throw new McpSandboxException($"No sandbox body registered for tool '{toolName}'.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            return await body(inputJson, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw;
        }
    }

    /// <summary>
    /// Helper for tool bodies that wrap an outbound connector call. Returns
    /// true iff the candidate path matches any of the supplied regex
    /// patterns. Patterns are anchored implicitly (they're used inside
    /// <see cref="Regex.IsMatch(string, string)"/> with <see cref="RegexOptions.IgnoreCase"/>).
    /// </summary>
    public static bool CheckConnector(string candidatePath, IReadOnlyList<string> allowedPatterns)
    {
        if (string.IsNullOrWhiteSpace(candidatePath)) return false;
        foreach (var pattern in allowedPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            try
            {
                if (Regex.IsMatch(candidatePath, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)))
                    return true;
            }
            catch (RegexMatchTimeoutException) { /* treat as no-match; never throw out of the gate */ }
            catch (ArgumentException) { /* malformed pattern — treat as no-match */ }
        }
        return false;
    }
}

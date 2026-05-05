namespace RadioPad.Application.Abstractions;

/// <summary>
/// Iter-31 MCP-006 — sandbox abstraction for executing custom MCP tool
/// bodies. Implementations enforce a hard wall-clock timeout and block file
/// or network access. The v0.1 in-process sandbox supports only pure
/// JSON-in / JSON-out delegates registered by the application; a future
/// iteration may swap in a WASM (Wasmtime) sandbox without changing this
/// surface.
/// </summary>
public interface IMcpSandbox
{
    /// <summary>
    /// Run the registered tool delegate with <paramref name="inputJson"/> as
    /// its sole argument. Returns the delegate's JSON output. Throws
    /// <see cref="OperationCanceledException"/> on timeout (>=5s by default)
    /// and <see cref="McpSandboxException"/> on disallowed connector access
    /// or any other policy breach.
    /// </summary>
    Task<string> InvokeAsync(
        string toolName,
        string inputJson,
        IReadOnlyList<string> allowedConnectorPatterns,
        TimeSpan timeout,
        CancellationToken ct);

    /// <summary>
    /// Register a JSON-in / JSON-out delegate under <paramref name="toolName"/>.
    /// Idempotent — re-registering the same name overwrites the previous
    /// delegate. Returns the registry instance for chaining.
    /// </summary>
    IMcpSandbox Register(string toolName, Func<string, CancellationToken, Task<string>> body);
}

public sealed class McpSandboxException : Exception
{
    public McpSandboxException(string message) : base(message) { }
    public McpSandboxException(string message, Exception inner) : base(message, inner) { }
}

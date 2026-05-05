using Microsoft.Extensions.Logging;
using RadioPad.Application.Security;

namespace RadioPad.Api.Logging;

/// <summary>
/// Iter-31 SEC-010 — wraps every <see cref="ILogger"/> produced by the inner
/// providers and redacts PHI-shaped substrings from formatted log messages
/// before they reach any sink (console, file, SIEM exporter). Exceptions are
/// re-projected via a thin wrapper so their message text is also scrubbed —
/// stack traces are left intact since they describe code locations, not PHI.
/// </summary>
public sealed class PhiRedactingLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _inner;
    public PhiRedactingLoggerProvider(ILoggerProvider inner) => _inner = inner;
    public ILogger CreateLogger(string categoryName) => new PhiRedactingLogger(_inner.CreateLogger(categoryName));
    public void Dispose() => _inner.Dispose();

    private sealed class PhiRedactingLogger : ILogger
    {
        private readonly ILogger _inner;
        public PhiRedactingLogger(ILogger inner) => _inner = inner;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _inner.Log(logLevel, eventId, state, exception, (s, e) =>
            {
                var raw = formatter(s, e);
                return PhiRedactor.Redact(raw);
            });
        }
    }
}

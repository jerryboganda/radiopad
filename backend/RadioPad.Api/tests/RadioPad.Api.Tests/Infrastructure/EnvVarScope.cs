namespace RadioPad.Api.Tests.Infrastructure;

/// <summary>
/// Disposable helper that saves, sets, and restores a single environment
/// variable. Use <c>using var env = EnvVarScope.Set("NAME", "value");</c>
/// inside tests that mutate process-wide env vars so parallel / sequential
/// test runs never leak state.
/// </summary>
internal sealed class EnvVarScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    private EnvVarScope(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>
    /// Sets <paramref name="name"/> to <paramref name="value"/> and returns
    /// a scope that restores the original value on <see cref="Dispose"/>.
    /// Pass <c>null</c> as <paramref name="value"/> to clear the variable.
    /// </summary>
    public static EnvVarScope Set(string name, string? value) => new(name, value);

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}

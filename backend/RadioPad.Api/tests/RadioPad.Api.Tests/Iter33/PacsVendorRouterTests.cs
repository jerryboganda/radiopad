using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Services.Pacs;
using RadioPad.Infrastructure.Pacs;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 INT-007 — <see cref="PacsVendorRouter"/> picks the keyed adapter
/// matching <c>TenantSettings.PacsVendor</c>; null / empty / unknown vendor
/// returns <c>null</c> so the caller falls back to the generic DICOMweb path.
/// </summary>
public class PacsVendorRouterTests
{
    private sealed class NullHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static IServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory, NullHttp>();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLoggerForwarder<>));
        services.AddKeyedSingleton<IPacsVendorAdapter, SectraIds7Adapter>("sectra");
        services.AddKeyedSingleton<IPacsVendorAdapter, Visage7Adapter>("visage");
        services.AddKeyedSingleton<IPacsVendorAdapter, CarestreamVueAdapter>("carestream");
        services.AddSingleton<IPacsVendorRouter, PacsVendorRouter>();
        return services.BuildServiceProvider();
    }

    private sealed class NullLoggerForwarder<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }

    [Fact]
    public void Resolve_Visage_Returns_Visage_Adapter()
    {
        var sp = BuildSp();
        var router = sp.GetRequiredService<IPacsVendorRouter>();
        var a = router.Resolve("visage");
        Assert.NotNull(a);
        Assert.IsType<Visage7Adapter>(a);
        Assert.Equal("visage", a!.Vendor);
    }

    [Fact]
    public void Resolve_Sectra_And_Carestream_Pick_Correct_Adapters()
    {
        var sp = BuildSp();
        var router = sp.GetRequiredService<IPacsVendorRouter>();
        Assert.IsType<SectraIds7Adapter>(router.Resolve("sectra"));
        Assert.IsType<CarestreamVueAdapter>(router.Resolve("carestream"));
        // case-insensitive
        Assert.IsType<SectraIds7Adapter>(router.Resolve("SECTRA"));
    }

    [Fact]
    public void Resolve_Null_Or_Unknown_Returns_Null_For_Default_Path()
    {
        var sp = BuildSp();
        var router = sp.GetRequiredService<IPacsVendorRouter>();
        Assert.Null(router.Resolve(null));
        Assert.Null(router.Resolve(""));
        Assert.Null(router.Resolve("   "));
        Assert.Null(router.Resolve("orthanc"));   // not a registered vendor
    }
}

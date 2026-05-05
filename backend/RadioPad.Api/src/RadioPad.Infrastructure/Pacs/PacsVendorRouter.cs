using Microsoft.Extensions.DependencyInjection;
using RadioPad.Application.Services.Pacs;

namespace RadioPad.Infrastructure.Pacs;

/// <summary>
/// Iter-33 INT-007 — resolves the per-tenant <see cref="IPacsVendorAdapter"/>
/// from <c>TenantSettings.PacsVendor</c>. Returns <c>null</c> when the
/// tenant has not selected a vendor; callers fall back to the default
/// DICOMweb path with a warning.
/// </summary>
public sealed class PacsVendorRouter : IPacsVendorRouter
{
    private readonly IServiceProvider _sp;

    public PacsVendorRouter(IServiceProvider sp) => _sp = sp;

    public IPacsVendorAdapter? Resolve(string? pacsVendor)
    {
        if (string.IsNullOrWhiteSpace(pacsVendor)) return null;
        var key = pacsVendor.Trim().ToLowerInvariant();
        return key switch
        {
            "sectra" or "visage" or "carestream"
                => _sp.GetKeyedService<IPacsVendorAdapter>(key),
            _ => null,
        };
    }
}

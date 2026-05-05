using RadioPad.Domain.Entities;

namespace RadioPad.Application.Abstractions;

/// <summary>
/// Iter-35 PROV-007 — adapter that exchanges a stored refresh token for a
/// fresh access + refresh-token pair against an upstream OAuth-compliant
/// IdP. The refresh-vault and the rotation background service depend on
/// this contract; concrete implementations land alongside the relevant
/// provider adapter (e.g. an Azure-AD or Auth0 issuer in iter-36).
///
/// The default registration is <c>NoopOAuthTokenIssuer</c> which advertises
/// <see cref="CanRefresh"/> = <c>false</c> so the rotation worker skips
/// quietly until a real adapter is wired.
/// </summary>
public interface IOAuthTokenIssuer
{
    /// <summary>
    /// True when the issuer is fully configured and can perform a refresh.
    /// The rotation worker uses this to short-circuit before logging.
    /// </summary>
    bool CanRefresh { get; }

    /// <summary>
    /// Exchange <paramref name="currentRefreshToken"/> for a new pair against
    /// the IdP referenced by <paramref name="provider"/>. Implementations
    /// MUST NOT log or audit the token bytes.
    /// </summary>
    Task<(string token, DateTimeOffset? expiresAt)> RefreshAsync(
        ProviderConfig provider,
        string currentRefreshToken,
        CancellationToken ct);
}

/// <summary>
/// Default no-op issuer. Returns <see cref="CanRefresh"/> = <c>false</c>;
/// <see cref="RefreshAsync"/> throws to make accidental use loud.
/// </summary>
public sealed class NoopOAuthTokenIssuer : IOAuthTokenIssuer
{
    public bool CanRefresh => false;

    public Task<(string token, DateTimeOffset? expiresAt)> RefreshAsync(
        ProviderConfig provider,
        string currentRefreshToken,
        CancellationToken ct)
        => throw new NotSupportedException(
            "NoopOAuthTokenIssuer cannot refresh tokens. Register a real IOAuthTokenIssuer.");
}

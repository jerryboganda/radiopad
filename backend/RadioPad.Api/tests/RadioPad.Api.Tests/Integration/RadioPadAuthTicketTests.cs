using RadioPad.Api.Auth;
using RadioPad.Api.Tests.Infrastructure;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// AUTH-003 — the short-lived <c>mfa-setup</c> ticket is the cryptographic gate
/// that authorizes forced first-login TOTP enrolment before a session exists.
/// These tests pin the guarantees the controller relies on (it accepts the
/// ticket in lieu of a bearer), now that the bearer middleware passes
/// <c>/api/auth/mfa/*</c> through.
/// </summary>
public sealed class RadioPadAuthTicketTests
{
    private const string Secret = "test-secret-with-at-least-thirty-two-chars";

    [Fact]
    public void Mfa_Setup_Ticket_Round_Trips()
    {
        using var scope = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", Secret);
        var token = RadioPadBearerTokens.MintTicket(RadioPadBearerTokens.MfaSetupScope, "acme", "u@acme.test");
        Assert.True(RadioPadBearerTokens.TryValidateTicket(token, RadioPadBearerTokens.MfaSetupScope, null, out var t, out var u));
        Assert.Equal("acme", t);
        Assert.Equal("u@acme.test", u);
    }

    [Fact]
    public void Ticket_With_Wrong_Scope_Is_Rejected()
    {
        using var scope = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", Secret);
        var token = RadioPadBearerTokens.MintTicket(RadioPadBearerTokens.MfaSetupScope, "acme", "u@acme.test");
        Assert.False(RadioPadBearerTokens.TryValidateTicket(token, "some-other-scope", null, out _, out _));
    }

    [Fact]
    public void Tampered_Ticket_Is_Rejected()
    {
        using var scope = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", Secret);
        var token = RadioPadBearerTokens.MintTicket(RadioPadBearerTokens.MfaSetupScope, "acme", "u@acme.test");
        var tampered = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');
        Assert.False(RadioPadBearerTokens.TryValidateTicket(tampered, RadioPadBearerTokens.MfaSetupScope, null, out _, out _));
    }

    [Fact]
    public void Expired_Ticket_Is_Rejected()
    {
        using var scope = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", Secret);
        var issued = DateTimeOffset.UtcNow;
        var token = RadioPadBearerTokens.MintTicket(RadioPadBearerTokens.MfaSetupScope, "acme", "u@acme.test", now: issued);
        Assert.False(RadioPadBearerTokens.TryValidateTicket(token, RadioPadBearerTokens.MfaSetupScope, null, out _, out _, now: issued.AddMinutes(11)));
    }

    [Fact]
    public void Session_Bearer_Is_Not_Accepted_As_Ticket()
    {
        using var scope = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", Secret);
        var bearer = RadioPadBearerTokens.Mint("acme", "u@acme.test", 0);
        Assert.False(RadioPadBearerTokens.TryValidateTicket(bearer, RadioPadBearerTokens.MfaSetupScope, null, out _, out _));
    }

    [Fact]
    public void Ticket_Is_Not_Accepted_As_Session_Bearer()
    {
        using var scope = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", Secret);
        var token = RadioPadBearerTokens.MintTicket(RadioPadBearerTokens.MfaSetupScope, "acme", "u@acme.test");
        Assert.False(RadioPadBearerTokens.TryValidate(token, "acme", "u@acme.test", 0, null, out _));
    }
}

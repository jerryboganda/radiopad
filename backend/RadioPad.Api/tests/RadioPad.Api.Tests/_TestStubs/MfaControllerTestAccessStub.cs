namespace RadioPad.Api.Controllers;

/// <summary>
/// Thin test accessor that forwards to the internal RFC 4648 / RFC 6238
/// helpers on <see cref="MfaController"/>. Production code remains the
/// single source of truth; this exists purely so the integration test
/// can compute an expected TOTP from the enrollment secret.
/// </summary>
internal static class MfaController_TestAccess
{
    public static byte[] B32Decode(string s) => MfaController.Base32Decode(s);
    public static string Hotp(byte[] key, long counter) => MfaController.HotpAt(key, counter);
}

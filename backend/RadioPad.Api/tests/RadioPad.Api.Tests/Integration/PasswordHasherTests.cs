using RadioPad.Api.Auth;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>AUTH-001 — PBKDF2 password hashing round-trip + negative cases.</summary>
public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_Then_Verify_Succeeds()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        Assert.StartsWith("pbkdf2$", hash);
        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_Rejects_Wrong_Password()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        Assert.False(PasswordHasher.Verify("Correct Horse Battery Staple", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Verify_Rejects_Empty_Or_Malformed_Stored_Hash()
    {
        Assert.False(PasswordHasher.Verify("anything", null));
        Assert.False(PasswordHasher.Verify("anything", ""));
        Assert.False(PasswordHasher.Verify("anything", "not-a-real-hash"));
        Assert.False(PasswordHasher.Verify("anything", "pbkdf2$abc$def$ghi"));
    }

    [Fact]
    public void Each_Hash_Uses_A_Fresh_Salt()
    {
        var a = PasswordHasher.Hash("same-password-12chars");
        var b = PasswordHasher.Hash("same-password-12chars");
        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("same-password-12chars", a));
        Assert.True(PasswordHasher.Verify("same-password-12chars", b));
    }

    [Fact]
    public void Generated_Temporary_Password_Meets_Minimum_Length()
    {
        var temp = PasswordHasher.GenerateTemporaryPassword();
        Assert.True(temp.Length >= PasswordHasher.MinLength);
        Assert.True(PasswordHasher.Verify(temp, PasswordHasher.Hash(temp)));
    }
}

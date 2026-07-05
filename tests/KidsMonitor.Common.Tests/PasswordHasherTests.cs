using KidsMonitor.Common.Security;

namespace KidsMonitor.Common.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.True(PasswordHasher.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.False(PasswordHasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_ProducesDifferentSaltEachTime()
    {
        var hash1 = PasswordHasher.Hash("same-password");
        var hash2 = PasswordHasher.Hash("same-password");

        Assert.False(hash1.AsSpan().SequenceEqual(hash2));
        Assert.True(PasswordHasher.Verify("same-password", hash1));
        Assert.True(PasswordHasher.Verify("same-password", hash2));
    }

    [Fact]
    public void Verify_MalformedBlob_ReturnsFalse()
    {
        Assert.False(PasswordHasher.Verify("anything", new byte[] { 1, 2, 3 }));
    }
}

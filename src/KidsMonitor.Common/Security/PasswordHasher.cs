using System.Security.Cryptography;

namespace KidsMonitor.Common.Security;

/// <summary>
/// PBKDF2-SHA256 password hashing shared by the setup and unlock flows. Verification always
/// happens server-side in the Service; this type has no notion of "where" it runs.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    /// <summary>Hashes a password into a self-describing blob: [iterations:4][salt:16][hash:32].</summary>
    public static byte[] Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        var blob = new byte[4 + SaltSize + HashSize];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 4), Iterations);
        salt.CopyTo(blob, 4);
        hash.CopyTo(blob, 4 + SaltSize);
        return blob;
    }

    /// <summary>Verifies a password against a blob produced by <see cref="Hash"/>.</summary>
    public static bool Verify(string password, byte[] stored)
    {
        if (stored.Length != 4 + SaltSize + HashSize)
        {
            return false;
        }

        var iterations = BitConverter.ToInt32(stored, 0);
        var salt = stored.AsSpan(4, SaltSize).ToArray();
        var expectedHash = stored.AsSpan(4 + SaltSize, HashSize).ToArray();

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

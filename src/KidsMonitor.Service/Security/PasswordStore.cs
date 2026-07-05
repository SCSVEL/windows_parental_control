using KidsMonitor.Common.Security;

namespace KidsMonitor.Service.Security;

/// <summary>
/// Loads/persists the hashed parent password at password.dat. Verification and the
/// "does a password already exist" check both live here; the connecting client's admin
/// membership (required only for the very first SetPasswordRequest) is checked separately
/// by the pipe server, since that's a property of the connection, not the password itself.
/// </summary>
public sealed class PasswordStore
{
    private readonly string _path;
    private byte[]? _hash;

    public PasswordStore(string path)
    {
        _path = path;
        if (File.Exists(_path))
        {
            _hash = File.ReadAllBytes(_path);
        }
    }

    public bool IsSetupRequired => _hash is null;

    public bool Verify(string password) => _hash is not null && PasswordHasher.Verify(password, _hash);

    public bool TrySetPassword(string? currentPassword, string newPassword, out string? error)
    {
        if (_hash is not null && (currentPassword is null || !PasswordHasher.Verify(currentPassword, _hash)))
        {
            error = "Current password is incorrect.";
            return false;
        }

        var hash = PasswordHasher.Hash(newPassword);
        File.WriteAllBytes(_path, hash);
        _hash = hash;
        error = null;
        return true;
    }
}

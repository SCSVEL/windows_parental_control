using Microsoft.Win32;

namespace KidsMonitor.Service.Enforcement;

/// <summary>
/// Toggles the DisableTaskMgr policy in the locked child's own registry hive (HKEY_USERS\{SID})
/// while the overlay is engaged. Set idempotently -- safe to call Disable/Enable repeatedly.
/// </summary>
internal static class TaskManagerPolicy
{
    private const string PolicyKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string PolicyValueName = "DisableTaskMgr";

    public static void Disable(string userSid) => SetValue(userSid, 1);

    public static void Enable(string userSid) => SetValue(userSid, 0);

    private static void SetValue(string userSid, int value)
    {
        using var userHive = Registry.Users.OpenSubKey(userSid, writable: true);
        if (userHive is null)
        {
            return;
        }

        using var policyKey = userHive.CreateSubKey(PolicyKeyPath, writable: true);
        policyKey.SetValue(PolicyValueName, value, RegistryValueKind.DWord);
    }
}

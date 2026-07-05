using System.Security.AccessControl;
using System.Security.Principal;

namespace KidsMonitor.Service.Security;

/// <summary>
/// Idempotently restricts C:\ProgramData\KidsMonitor to SYSTEM + Administrators, so a
/// standard-user child can't read password.dat or tamper with config/state. Applied on every
/// service startup rather than via a fragile installer custom action.
/// </summary>
public static class ProgramDataAcl
{
    public static void Lock(string directoryPath)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }
}

using System.IO.Pipes;
using System.Security.Principal;

namespace KidsMonitor.Service.Security;

/// <summary>Checks whether the connected pipe client's token is in local Administrators.</summary>
internal static class PipeClientAuth
{
    public static bool ConnectedClientIsAdministrator(NamedPipeServerStream pipe)
    {
        var isAdmin = false;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        });
        return isAdmin;
    }
}

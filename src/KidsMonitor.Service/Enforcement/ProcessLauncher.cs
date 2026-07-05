using System.Runtime.InteropServices;
using System.Security.Principal;

namespace KidsMonitor.Service.Enforcement;

/// <summary>
/// Launches a process on the child's interactive desktop from a LocalSystem service, via the
/// WTSQueryUserToken -> DuplicateTokenEx -> CreateProcessAsUser chain described in the README.
/// </summary>
internal static class ProcessLauncher
{
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint INVALID_SESSION_ID = 0xFFFFFFFF;

    public static bool TryLaunchInActiveSession(string executablePath, out int processId, out int win32Error)
    {
        processId = 0;
        win32Error = 0;

        EnablePrivilege("SeIncreaseQuotaPrivilege");
        EnablePrivilege("SeAssignPrimaryTokenPrivilege");

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == INVALID_SESSION_ID)
        {
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            if (!DuplicateTokenEx(userToken, MAXIMUM_ALLOWED, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out var primaryToken))
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                var hasEnvBlock = CreateEnvironmentBlock(out var envBlock, primaryToken, false);
                try
                {
                    var startupInfo = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf<STARTUPINFO>(),
                        lpDesktop = "winsta0\\default",
                    };

                    var created = CreateProcessAsUser(
                        primaryToken,
                        executablePath,
                        lpCommandLine: null,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        bInheritHandles: false,
                        CREATE_UNICODE_ENVIRONMENT,
                        hasEnvBlock ? envBlock : IntPtr.Zero,
                        Path.GetDirectoryName(executablePath),
                        ref startupInfo,
                        out var processInfo);

                    if (!created)
                    {
                        win32Error = Marshal.GetLastWin32Error();
                        return false;
                    }

                    processId = processInfo.dwProcessId;
                    CloseHandle(processInfo.hThread);
                    CloseHandle(processInfo.hProcess);
                    return true;
                }
                finally
                {
                    if (hasEnvBlock)
                    {
                        DestroyEnvironmentBlock(envBlock);
                    }
                }
            }
            finally
            {
                CloseHandle(primaryToken);
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    public static string? GetActiveSessionUserSid()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == INVALID_SESSION_ID || !WTSQueryUserToken(sessionId, out var token))
        {
            return null;
        }

        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User?.Value;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static void EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var tokenHandle))
        {
            return;
        }

        try
        {
            if (!LookupPrivilegeValue(null, privilegeName, out var luid))
            {
                return;
            }

            var privileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED,
            };

            AdjustTokenPrivileges(tokenHandle, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation,
    }

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation = 2,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes, SECURITY_IMPERSONATION_LEVEL impersonationLevel, TOKEN_TYPE tokenType, out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr hToken, string lpApplicationName, string? lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);
}

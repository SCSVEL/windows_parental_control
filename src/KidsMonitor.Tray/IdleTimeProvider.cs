using System.Runtime.InteropServices;

namespace KidsMonitor_Tray;

internal static class IdleTimeProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var idleMilliseconds = (uint)Environment.TickCount - info.dwTime;
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }
}

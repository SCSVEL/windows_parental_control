using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace KidsMonitor_Overlay;

/// <summary>
/// The application window. Borderless, always-on-top, and sized to span every monitor so
/// the child can't drag another window onto an uncovered screen. Only closed by the Service
/// killing the process (or, later, a verified unlock) -- never by the user.
///
/// Uses raw Win32 window APIs (SetWindowLongPtr/SetWindowPos) rather than WinAppSDK's
/// AppWindow/OverlappedPresenter -- those reliably fail-fast-crash (0xc000027b) for
/// unpackaged WinAppSDK apps like this one (see microsoft/WindowsAppSDK#1815, #2301, #8446).
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly KeyboardHook _keyboardHook = new();

    public MainWindow()
    {
        InitializeComponent();

        RootFrame.Navigate(typeof(MainPage));

        var hwnd = WindowNative.GetWindowHandle(this);
        MakeBorderlessTopmostFullScreen(hwnd);

        _keyboardHook.Install();
        Closed += (_, _) => _keyboardHook.Uninstall();
    }

    private static void MakeBorderlessTopmostFullScreen(IntPtr hwnd)
    {
        var style = (long)GetWindowLongPtrW(hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        SetWindowLongPtrW(hwnd, GWL_STYLE, (IntPtr)style);

        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    }

    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;
    private const long WS_MINIMIZEBOX = 0x00020000;
    private const long WS_MAXIMIZEBOX = 0x00010000;
    private const long WS_SYSMENU = 0x00080000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}

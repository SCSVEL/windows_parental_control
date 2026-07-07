using CommunityToolkit.Mvvm.ComponentModel;

namespace KidsMonitor_Tray;

public partial class StatusViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Connecting to KidsMonitor service...";

    [ObservableProperty]
    private bool _setupRequired;

    [ObservableProperty]
    private int _limitMinutes;

    [ObservableProperty]
    private int _breakIntervalMinutes;

    [ObservableProperty]
    private int _breakDurationMinutes = 10;

    public void UpdateStatus(int usedSeconds, int limitSeconds, bool setupRequired, int breakIntervalMinutes = 0, int breakDurationMinutes = 10)
    {
        SetupRequired = setupRequired;

        if (setupRequired)
        {
            StatusText = "Setup required -- see the setup window";
            return;
        }

        var used = (int)TimeSpan.FromSeconds(usedSeconds).TotalMinutes;
        var limit = (int)TimeSpan.FromSeconds(limitSeconds).TotalMinutes;
        LimitMinutes = limit;
        BreakIntervalMinutes = breakIntervalMinutes;
        BreakDurationMinutes = breakDurationMinutes;
        StatusText = $"{used} of {limit} min used today";
    }

    public void ShowDisconnected()
    {
        StatusText = "Not connected to KidsMonitor service";
    }
}

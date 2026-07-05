using CommunityToolkit.Mvvm.ComponentModel;

namespace KidsMonitor_Tray;

public partial class StatusViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Connecting to KidsMonitor service...";

    public void UpdateStatus(int usedSeconds, int limitSeconds)
    {
        var used = (int)TimeSpan.FromSeconds(usedSeconds).TotalMinutes;
        var limit = (int)TimeSpan.FromSeconds(limitSeconds).TotalMinutes;
        StatusText = $"{used} of {limit} min used today";
    }

    public void ShowDisconnected()
    {
        StatusText = "Not connected to KidsMonitor service";
    }
}

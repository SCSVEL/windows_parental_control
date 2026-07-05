using System.Text.Json;
using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace KidsMonitor_Overlay;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();

        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                _ = TryUnlockAsync();
            }
        };
    }

    private async void UnlockButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await TryUnlockAsync();

    private async Task TryUnlockAsync()
    {
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        UnlockButton.IsEnabled = false;
        ErrorText.Text = string.Empty;

        try
        {
            await using var client = new KidsMonitorPipeClient();
            await client.ConnectAsync(timeoutMs: 3000);

            await client.SendAsync(nameof(UnlockRequest), new UnlockRequest(password));
            var envelope = await client.ReadMessageAsync();
            var result = envelope is null ? null : JsonSerializer.Deserialize<OperationResult>(envelope.Payload);

            if (result is not { Success: true })
            {
                ErrorText.Text = result?.Error ?? "Incorrect password.";
                PasswordBox.Password = string.Empty;
            }

            // On success the Service kills this process itself -- nothing more to do here.
        }
        catch
        {
            ErrorText.Text = "Could not reach the KidsMonitor service.";
        }
        finally
        {
            UnlockButton.IsEnabled = true;
        }
    }
}

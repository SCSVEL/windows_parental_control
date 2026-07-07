using System.Text.Json;
using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;
using Microsoft.UI.Xaml;
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

        // So a parent can start typing the password immediately without having to click into
        // the field first -- this is a full-screen lock screen, there's nothing else to click.
        Loaded += (_, _) => PasswordBox.Focus(FocusState.Programmatic);

        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                _ = TryUnlockAsync();
            }
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter as string == "break")
        {
            HeadingText.Text = "Break time!";
            SubtitleText.Text = "Take a short break -- this will unlock on its own soon. Ask a parent if you need it sooner.";
        }
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
            ErrorText.Text = "Can't connect to the KidsMonitor service. Make sure it's running and try again.";
        }
        finally
        {
            UnlockButton.IsEnabled = true;
        }
    }
}

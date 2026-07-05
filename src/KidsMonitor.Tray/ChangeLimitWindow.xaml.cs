using System.Text.Json;
using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;
using Microsoft.UI.Xaml;

namespace KidsMonitor_Tray;

/// <summary>Lets a parent change the daily limit; the Service requires the current password.</summary>
public sealed partial class ChangeLimitWindow : Window
{
    public ChangeLimitWindow()
    {
        InitializeComponent();
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var limitMinutes = (int)LimitBox.Value;

        if (string.IsNullOrEmpty(password))
        {
            ErrorText.Text = "Enter the current password.";
            return;
        }

        SubmitButton.IsEnabled = false;
        ErrorText.Text = string.Empty;

        try
        {
            await using var client = new KidsMonitorPipeClient();
            await client.ConnectAsync(timeoutMs: 3000);

            await client.SendAsync(nameof(SetLimitsRequest), new SetLimitsRequest(password, limitMinutes));
            var envelope = await client.ReadMessageAsync();
            var result = envelope is null ? null : JsonSerializer.Deserialize<OperationResult>(envelope.Payload);

            if (result is not { Success: true })
            {
                ErrorText.Text = result?.Error ?? "Could not change the limit.";
                return;
            }

            Close();
        }
        catch
        {
            ErrorText.Text = "Can't connect to the KidsMonitor service. Make sure it's running and try again.";
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }
}

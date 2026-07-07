using System.Text.Json;
using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;
using Microsoft.UI.Xaml;

namespace KidsMonitor_Tray;

/// <summary>Lets a parent change the daily limit and/or password; the Service requires the current password for both.</summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(int currentLimitMinutes, int currentBreakIntervalMinutes = 0, int currentBreakDurationMinutes = 10, int currentIdleResetSeconds = 180)
    {
        InitializeComponent();
        if (currentLimitMinutes > 0)
        {
            LimitBox.Value = currentLimitMinutes;
        }

        BreakIntervalBox.Value = currentBreakIntervalMinutes;
        if (currentBreakDurationMinutes > 0)
        {
            BreakDurationBox.Value = currentBreakDurationMinutes;
        }

        if (currentIdleResetSeconds > 0)
        {
            IdleResetBox.Value = currentIdleResetSeconds;
        }
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var limitMinutes = (int)LimitBox.Value;
        var breakIntervalMinutes = (int)BreakIntervalBox.Value;
        var breakDurationMinutes = (int)BreakDurationBox.Value;
        var idleResetSeconds = (int)IdleResetBox.Value;
        var newPassword = NewPasswordBox.Password;
        var confirmNewPassword = ConfirmNewPasswordBox.Password;

        if (string.IsNullOrEmpty(password))
        {
            ErrorText.Text = "Enter the current password.";
            return;
        }

        if (!string.IsNullOrEmpty(newPassword) && newPassword != confirmNewPassword)
        {
            ErrorText.Text = "New passwords don't match.";
            return;
        }

        SubmitButton.IsEnabled = false;
        ErrorText.Text = string.Empty;

        try
        {
            await using var client = new KidsMonitorPipeClient();
            await client.ConnectAsync(timeoutMs: 3000);

            await client.SendAsync(nameof(SetLimitsRequest), new SetLimitsRequest(password, limitMinutes, breakIntervalMinutes, breakDurationMinutes, idleResetSeconds));
            var limitsResult = await ReadResultAsync(client);
            if (limitsResult is not { Success: true })
            {
                ErrorText.Text = limitsResult?.Error ?? "Could not change the limit.";
                return;
            }

            if (!string.IsNullOrEmpty(newPassword))
            {
                await client.SendAsync(nameof(SetPasswordRequest), new SetPasswordRequest(password, newPassword));
                var passwordResult = await ReadResultAsync(client);
                if (passwordResult is not { Success: true })
                {
                    ErrorText.Text = passwordResult?.Error ?? "Could not change the password.";
                    return;
                }
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

    private static async Task<OperationResult?> ReadResultAsync(KidsMonitorPipeClient client)
    {
        var envelope = await client.ReadMessageAsync();
        return envelope is null ? null : JsonSerializer.Deserialize<OperationResult>(envelope.Payload);
    }
}

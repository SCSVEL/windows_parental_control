using System.Text.Json;
using KidsMonitor.Common.Ipc;
using KidsMonitor.Common.Ipc.Messages;
using Microsoft.UI.Xaml;

namespace KidsMonitor_Tray;

/// <summary>
/// Mandatory first-run wizard: collects the daily limit and parent password, then sets both
/// over the pipe. The first SetPasswordRequest only succeeds if this process's token is in
/// local Administrators (enforced server-side), which is why setup must be run by the parent.
/// </summary>
public sealed partial class FirstRunSetupWindow : Window
{
    public FirstRunSetupWindow()
    {
        InitializeComponent();
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var confirm = ConfirmPasswordBox.Password;
        var limitMinutes = (int)LimitBox.Value;

        if (string.IsNullOrWhiteSpace(password))
        {
            ErrorText.Text = "Enter a password.";
            return;
        }

        if (password != confirm)
        {
            ErrorText.Text = "Passwords don't match.";
            return;
        }

        SubmitButton.IsEnabled = false;
        ErrorText.Text = string.Empty;

        try
        {
            await using var client = new KidsMonitorPipeClient();
            await client.ConnectAsync(timeoutMs: 3000);

            await client.SendAsync(nameof(SetPasswordRequest), new SetPasswordRequest(null, password));
            var passwordResult = await ReadResultAsync(client);
            if (passwordResult is not { Success: true })
            {
                ErrorText.Text = passwordResult?.Error ?? "Could not set the password. Make sure you're running as an administrator.";
                return;
            }

            await client.SendAsync(nameof(SetLimitsRequest), new SetLimitsRequest(password, limitMinutes));
            var limitsResult = await ReadResultAsync(client);
            if (limitsResult is not { Success: true })
            {
                ErrorText.Text = limitsResult?.Error ?? "Could not set the daily limit.";
                return;
            }

            Close();
        }
        catch
        {
            ErrorText.Text = "Could not reach the KidsMonitor service. Make sure it's running.";
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

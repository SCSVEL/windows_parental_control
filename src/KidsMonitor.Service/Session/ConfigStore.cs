using System.Text.Json;

namespace KidsMonitor.Service.Session;

/// <summary>Persists parent-configured limits to config.json so they survive service restarts.</summary>
public sealed class ConfigStore(string path)
{
    /// <summary>Null if config.json doesn't exist yet, so callers can fall back to appsettings.json.</summary>
    public int? ReadDailyLimitMinutes() => ReadFile()?.DailyLimitMinutes;

    /// <summary>0 means breaks are disabled; also the default when config.json predates this field.</summary>
    public int ReadBreakIntervalMinutes() => ReadFile()?.BreakIntervalMinutes ?? 0;

    public int ReadBreakDurationMinutes() => ReadFile()?.BreakDurationMinutes ?? 10;

    /// <summary>Null if config.json doesn't have this field yet (predates it, or 0/unset), so callers fall back to appsettings.json.</summary>
    public int? ReadIdleResetSeconds() => ReadFile()?.IdleResetSeconds is int seconds and > 0 ? seconds : null;

    public void WriteDailyLimitMinutes(int minutes)
    {
        WriteFile((ReadFile() ?? DefaultData) with { DailyLimitMinutes = minutes });
    }

    public void WriteBreakSettings(int breakIntervalMinutes, int breakDurationMinutes)
    {
        WriteFile((ReadFile() ?? DefaultData) with
        {
            BreakIntervalMinutes = breakIntervalMinutes,
            BreakDurationMinutes = breakDurationMinutes,
        });
    }

    public void WriteIdleResetSeconds(int seconds)
    {
        WriteFile((ReadFile() ?? DefaultData) with { IdleResetSeconds = seconds });
    }

    private static readonly ConfigData DefaultData = new(120, 0, 10, 0);

    private ConfigData? ReadFile() => File.Exists(path) ? JsonSerializer.Deserialize<ConfigData>(File.ReadAllText(path)) : null;

    private void WriteFile(ConfigData data) => File.WriteAllText(path, JsonSerializer.Serialize(data));

    private sealed record ConfigData(int DailyLimitMinutes, int BreakIntervalMinutes = 0, int BreakDurationMinutes = 10, int IdleResetSeconds = 0);
}

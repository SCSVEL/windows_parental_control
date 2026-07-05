using System.Text.Json;

namespace KidsMonitor.Service.Session;

/// <summary>Persists the parent-configured daily limit to config.json so it survives service restarts.</summary>
public sealed class ConfigStore(string path)
{
    public int? ReadDailyLimitMinutes()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var data = JsonSerializer.Deserialize<ConfigData>(File.ReadAllText(path));
        return data?.DailyLimitMinutes;
    }

    public void WriteDailyLimitMinutes(int minutes)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(new ConfigData(minutes)));
    }

    private sealed record ConfigData(int DailyLimitMinutes);
}

using System.IO;
using System.Text.Json;
using Luminosity.Models;

namespace Luminosity.Services;

/// <summary>Persisted app state: per-monitor color values, autostart, per-app rules, window bounds.</summary>
public sealed class AppSettings
{
    public bool RunOnStartup { get; set; }

    /// <summary>Last-applied baseline values: monitor key -> (control type -> value).</summary>
    public Dictionary<string, Dictionary<string, int>> Monitors { get; set; } = new();

    /// <summary>Per-app color rules.</summary>
    public List<AppRule> AppRules { get; set; } = new();

    // Last window bounds (restored on launch).
    public double? WinWidth { get; set; }
    public double? WinHeight { get; set; }
    public double? WinLeft { get; set; }
    public double? WinTop { get; set; }
}

/// <summary>Loads/saves <see cref="AppSettings"/> to %APPDATA%\Luminosity\settings.json.</summary>
public sealed class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Luminosity");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            Settings = new AppSettings(); // corrupt file -> start fresh
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch
        {
            // best-effort; ignore disk errors
        }
    }

    public void StoreValue(string monitorKey, int controlType, int value)
    {
        if (!Settings.Monitors.TryGetValue(monitorKey, out var map))
            Settings.Monitors[monitorKey] = map = new Dictionary<string, int>();
        map[controlType.ToString()] = value;
    }

    public bool TryGetValue(string monitorKey, int controlType, out int value)
    {
        value = 0;
        return Settings.Monitors.TryGetValue(monitorKey, out var map)
            && map.TryGetValue(controlType.ToString(), out value);
    }
}

namespace Luminosity.Models;

/// <summary>
/// A per-app color rule: when <see cref="ProcessName"/> is running, the listed control values are
/// applied; when it exits, the pre-launch values are restored. Only the controls the user opted to
/// change are stored in <see cref="Values"/> — everything else is left untouched.
/// </summary>
public sealed class AppRule
{
    public string Name { get; set; } = "";

    /// <summary>Executable name, lowercase, without the ".exe" suffix (used for matching).</summary>
    public string ProcessName { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>monitor key -> (control type string -> absolute target value).</summary>
    public Dictionary<string, Dictionary<string, int>> Values { get; set; } = new();

    public int ControlCount => Values.Values.Sum(m => m.Count);
}

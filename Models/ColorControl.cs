namespace Luminosity.Models;

/// <summary>One adjustable color setting on a monitor (e.g. Saturation), with its value range.</summary>
public sealed class ColorControl
{
    public required int Type { get; init; }          // ControlType.* constant
    public required string Name { get; init; }        // "Saturation", "Brightness", ...
    public int Min { get; init; }
    public int Max { get; init; }
    public int Step { get; init; }
    public int Default { get; init; }
    public int Current { get; set; }

    /// <summary>Optional unit suffix shown next to the value (e.g. "K" for temperature).</summary>
    public string Unit { get; init; } = "";

    public static string DisplayName(int type) => ControlType.Name(type);
}

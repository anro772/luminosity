namespace Luminosity.Models;

/// <summary>
/// Canonical color-control identifiers, shared across backends so persisted settings stay
/// consistent. The first five match AMD's ADL_DISPLAY_COLOR_* constants; Gamma is Luminosity's
/// own (used by the universal gamma-ramp backend).
/// </summary>
public static class ControlType
{
    public const int Brightness = 1;
    public const int Contrast = 2;
    public const int Saturation = 4;
    public const int Hue = 8;
    public const int Temperature = 16;
    public const int Gamma = 32;

    /// <summary>Preferred display order in the UI.</summary>
    public static readonly int[] DisplayOrder =
        { Saturation, Brightness, Contrast, Gamma, Hue, Temperature };

    public static string Name(int type) => type switch
    {
        Saturation => "Saturation",
        Brightness => "Brightness",
        Contrast => "Contrast",
        Hue => "Hue",
        Temperature => "Temperature",
        Gamma => "Gamma",
        _ => $"Type {type}",
    };
}

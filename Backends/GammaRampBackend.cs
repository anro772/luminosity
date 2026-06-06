using Luminosity.Models;

namespace Luminosity.Backends;

/// <summary>
/// Universal fallback that works on any GPU (NVIDIA, Intel, AMD) via GDI gamma ramps
/// (<c>SetDeviceGammaRamp</c>). Per-channel LUTs can do Brightness, Contrast, Gamma, and color
/// Temperature — but not true Saturation/Hue (those need cross-channel mixing only vendor APIs
/// expose), so those controls are simply not offered here.
/// </summary>
public sealed class GammaRampBackend : IColorBackend
{
    public string Name => "Universal (gamma ramp)";

    public bool Initialize() => true; // GDI is always available on Windows

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        int index = 0;

        var dd = new GammaNative.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<GammaNative.DISPLAY_DEVICE>() };
        for (uint i = 0; GammaNative.EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            if ((dd.StateFlags & GammaNative.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
            {
                dd.cb = System.Runtime.InteropServices.Marshal.SizeOf<GammaNative.DISPLAY_DEVICE>();
                continue;
            }

            string deviceName = dd.DeviceName;               // e.g. \\.\DISPLAY1
            string gpu = dd.DeviceString;                    // adapter name
            bool primary = (dd.StateFlags & GammaNative.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;

            // Friendly monitor name (often generic) and current resolution.
            string monitorName = QueryMonitorName(deviceName);
            string res = QueryResolution(deviceName);

            index++;
            string sub = string.Join("  ·  ", new[] { res, gpu }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var monitor = new MonitorInfo
            {
                Key = $"gamma|{deviceName}",
                Title = primary ? $"Display {index} (primary)" : $"Display {index}",
                SubLabel = string.IsNullOrWhiteSpace(monitorName) ? sub : $"{monitorName}  ·  {sub}",
                BackendTag = deviceName,
            };

            monitor.Controls.Add(new ColorControl { Type = ControlType.Brightness,  Name = "Brightness",  Min = -100, Max = 100,  Step = 1,   Default = 0,    Current = 0 });
            monitor.Controls.Add(new ColorControl { Type = ControlType.Contrast,    Name = "Contrast",    Min = 0,    Max = 200,  Step = 1,   Default = 100,  Current = 100 });
            monitor.Controls.Add(new ColorControl { Type = ControlType.Gamma,       Name = "Gamma",       Min = 30,   Max = 280,  Step = 1,   Default = 100,  Current = 100 });
            monitor.Controls.Add(new ColorControl { Type = ControlType.Temperature, Name = "Temperature", Min = 4000, Max = 10000, Step = 100, Default = 6500, Current = 6500, Unit = "K" });

            result.Add(monitor);

            dd.cb = System.Runtime.InteropServices.Marshal.SizeOf<GammaNative.DISPLAY_DEVICE>();
        }

        return result;
    }

    public bool SetColor(MonitorInfo monitor, ColorControl control, int value)
    {
        if (monitor.BackendTag is not string deviceName) return false;

        control.Current = Math.Clamp(value, control.Min, control.Max);

        // Recompute the whole ramp from this monitor's current control values.
        int bright = ValueOf(monitor, ControlType.Brightness, 0);
        int contrast = ValueOf(monitor, ControlType.Contrast, 100);
        int gamma = ValueOf(monitor, ControlType.Gamma, 100);
        int temp = ValueOf(monitor, ControlType.Temperature, 6500);

        ushort[] ramp = BuildRamp(bright, contrast, gamma, temp);

        IntPtr hdc = GammaNative.CreateDC(null, deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try { return GammaNative.SetDeviceGammaRamp(hdc, ramp); }
        finally { GammaNative.DeleteDC(hdc); }
    }

    private static int ValueOf(MonitorInfo m, int type, int fallback)
        => m.Controls.FirstOrDefault(c => c.Type == type)?.Current ?? fallback;

    /// <summary>Builds a 3×256 gamma ramp from brightness/contrast/gamma/temperature.</summary>
    private static ushort[] BuildRamp(int brightnessVal, int contrastVal, int gammaVal, int tempK)
    {
        double brightness = brightnessVal / 100.0 * 0.5; // -0.5..+0.5 offset
        double contrast = contrastVal / 100.0;            // 1.0 neutral
        double gamma = Math.Max(0.10, gammaVal / 100.0);  // 1.0 neutral
        var (rs, gs, bs) = TemperatureScale(tempK);

        var ramp = new ushort[768];
        for (int i = 0; i < 256; i++)
        {
            double v = i / 255.0;
            v = Math.Pow(v, 1.0 / gamma);          // gamma
            v = (v - 0.5) * contrast + 0.5;        // contrast about midpoint
            v += brightness;                        // brightness offset
            v = Math.Clamp(v, 0.0, 1.0);

            ramp[i]       = ToU16(v * rs);
            ramp[256 + i] = ToU16(v * gs);
            ramp[512 + i] = ToU16(v * bs);
        }
        return ramp;
    }

    private static ushort ToU16(double v) => (ushort)(Math.Clamp(v, 0.0, 1.0) * 65535.0);

    /// <summary>Per-channel multipliers for a white point, normalized so 6500K is neutral.</summary>
    private static (double r, double g, double b) TemperatureScale(int kelvin)
    {
        var (r, g, b) = KelvinToRgb(kelvin);
        var (nr, ng, nb) = KelvinToRgb(6500);
        return (r / nr, g / ng, b / nb);
    }

    // Tanner Helland approximation (RGB 0..1 for a black-body temperature).
    private static (double r, double g, double b) KelvinToRgb(double kelvin)
    {
        double t = kelvin / 100.0;
        double r, g, b;

        r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661
                     : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;

        return (Math.Clamp(r, 1, 255) / 255.0,
                Math.Clamp(g, 1, 255) / 255.0,
                Math.Clamp(b, 1, 255) / 255.0);
    }

    private static string QueryMonitorName(string adapterDeviceName)
    {
        var mon = new GammaNative.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<GammaNative.DISPLAY_DEVICE>() };
        if (GammaNative.EnumDisplayDevices(adapterDeviceName, 0, ref mon, 0))
        {
            string name = mon.DeviceString?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(name) && !name.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return "";
    }

    private static string QueryResolution(string deviceName)
    {
        var dm = new GammaNative.DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<GammaNative.DEVMODE>() };
        if (GammaNative.EnumDisplaySettings(deviceName, GammaNative.ENUM_CURRENT_SETTINGS, ref dm) && dm.dmPelsWidth > 0)
            return $"{dm.dmPelsWidth}×{dm.dmPelsHeight}";
        return "";
    }

    public void Dispose() { }
}

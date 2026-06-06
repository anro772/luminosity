using Luminosity.Models;

namespace Luminosity.Backends;

/// <summary>
/// A source of per-monitor color control. Implementations wrap a vendor API (AMD ADL, NVIDIA
/// NVAPI, ...) or a universal mechanism (GDI gamma ramps). The active backend is chosen at
/// startup by <see cref="ColorService"/>.
/// </summary>
public interface IColorBackend : IDisposable
{
    /// <summary>Human-readable backend name shown in the UI (e.g. "AMD Radeon (ADL)").</summary>
    string Name { get; }

    /// <summary>Attempts to initialize. Returns false if this backend isn't usable on this system.</summary>
    bool Initialize();

    /// <summary>Enumerates connected displays and their supported controls. Called on (re)scan.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Applies a value to one control of one monitor. Returns true on success.</summary>
    bool SetColor(MonitorInfo monitor, ColorControl control, int value);
}

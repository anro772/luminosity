using Luminosity.Models;

namespace Luminosity.Backends;

/// <summary>
/// Selects and owns the active color backend. Tries vendor backends first (richer controls) and
/// falls back to the universal gamma-ramp backend so Luminosity works on any GPU. Adding a new
/// vendor (e.g. NVIDIA NVAPI) is just another <see cref="IColorBackend"/> in the candidate list.
/// </summary>
public sealed class ColorService : IDisposable
{
    private IColorBackend? _active;

    public string BackendName => _active?.Name ?? "none";
    public bool HasBackend => _active is not null;

    /// <summary>Initializes the first backend that works and reports adjustable monitors.</summary>
    public bool Initialize()
    {
        // Order matters: prefer hardware vendor APIs, then the universal fallback.
        Func<IColorBackend>[] candidates =
        {
            () => new AmdAdlBackend(),
            () => new GammaRampBackend(),
        };

        foreach (var make in candidates)
        {
            IColorBackend backend = make();
            try
            {
                if (backend.Initialize() && backend.GetMonitors().Count > 0)
                {
                    _active = backend;
                    return true;
                }
            }
            catch { /* try next */ }
            backend.Dispose();
        }
        return false;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors() => _active?.GetMonitors() ?? Array.Empty<MonitorInfo>();

    public bool SetColor(MonitorInfo monitor, ColorControl control, int value)
        => _active?.SetColor(monitor, control, value) ?? false;

    public void Dispose()
    {
        _active?.Dispose();
        _active = null;
    }
}

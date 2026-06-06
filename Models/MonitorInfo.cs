namespace Luminosity.Models;

/// <summary>
/// A connected display and the color controls it supports. Backend-agnostic: the owning backend
/// stashes whatever it needs to route Set calls in <see cref="BackendTag"/>.
/// </summary>
public sealed class MonitorInfo
{
    /// <summary>Stable key for matching saved settings/profiles across restarts.</summary>
    public required string Key { get; init; }

    /// <summary>Friendly label for the card header (e.g. monitor model).</summary>
    public required string Title { get; init; }

    /// <summary>Secondary line under the title (e.g. resolution / connector).</summary>
    public string SubLabel { get; init; } = "";

    /// <summary>Backend-specific routing token (cast by the backend that created this).</summary>
    public object? BackendTag { get; init; }

    public List<ColorControl> Controls { get; } = new();
}

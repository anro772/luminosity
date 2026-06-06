using System.Runtime.InteropServices;
using Luminosity.Adl;
using Luminosity.Models;

namespace Luminosity.Backends;

/// <summary>
/// AMD Radeon backend over the AMD Display Library (atiadlxx.dll). Provides true hardware
/// saturation/brightness/contrast/hue/temperature per display.
/// </summary>
public sealed class AmdAdlBackend : IColorBackend
{
    /// <summary>Routing token: which ADL adapter + display a monitor maps to.</summary>
    private sealed record AdlRef(int Adapter, int Display);

    public string Name => "AMD Radeon (ADL)";

    private IntPtr _context = IntPtr.Zero;
    private bool _initialized;

    // Keep the delegate rooted for the lifetime of the context.
    private readonly AdlNative.ADL_Main_Memory_Alloc _allocDelegate = Alloc;

    private static IntPtr Alloc(int size) => Marshal.AllocCoTaskMem(size);
    private static void Free(IntPtr ptr) { if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr); }

    private static readonly int[] ControlTypes =
    {
        AdlNative.ADL_DISPLAY_COLOR_SATURATION,
        AdlNative.ADL_DISPLAY_COLOR_BRIGHTNESS,
        AdlNative.ADL_DISPLAY_COLOR_CONTRAST,
        AdlNative.ADL_DISPLAY_COLOR_HUE,
        AdlNative.ADL_DISPLAY_COLOR_TEMPERATURE,
    };

    public bool Initialize()
    {
        if (_initialized) return true;
        try
        {
            int r = AdlNative.ADL2_Main_Control_Create(_allocDelegate, 1, ref _context);
            _initialized = r == AdlNative.ADL_OK && _context != IntPtr.Zero;
        }
        catch (DllNotFoundException) { _initialized = false; }
        catch (BadImageFormatException) { _initialized = false; }
        return _initialized;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        if (!_initialized) return result;

        int numAdapters = 0;
        if (AdlNative.ADL2_Adapter_NumberOfAdapters_Get(_context, ref numAdapters) != AdlNative.ADL_OK
            || numAdapters <= 0)
            return result;

        var adapters = GetAdapterInfo(numAdapters);
        var seenDisplays = new HashSet<int>();

        foreach (var adapter in adapters)
        {
            if (adapter.Present == 0 || adapter.Exist == 0)
                continue;

            foreach (var disp in GetDisplayInfo(adapter.AdapterIndex))
            {
                bool connected = (disp.DisplayInfoValue & AdlNative.ADL_DISPLAY_DISPLAYINFO_DISPLAYCONNECTED) != 0;
                bool mapped = (disp.DisplayInfoValue & AdlNative.ADL_DISPLAY_DISPLAYINFO_DISPLAYMAPPED) != 0;
                if (!connected || !mapped)
                    continue;

                int logicalDisplay = disp.DisplayID.DisplayLogicalIndex;
                if (!seenDisplays.Add(logicalDisplay))
                    continue; // same panel appears under several adapter entries

                int logicalAdapter = disp.DisplayID.DisplayLogicalAdapterIndex;
                string name = (disp.DisplayName ?? "").Trim();
                string manuf = (disp.DisplayManufacturerName ?? "").Trim();

                var monitor = new MonitorInfo
                {
                    Key = $"adl|{manuf}|{name}|{disp.DisplayConnector}",
                    Title = string.IsNullOrWhiteSpace($"{manuf} {name}".Trim())
                        ? $"Display {logicalDisplay}"
                        : $"{manuf} {name}".Trim(),
                    SubLabel = $"Display {logicalDisplay}",
                    BackendTag = new AdlRef(logicalAdapter, logicalDisplay),
                };

                PopulateControls(monitor, logicalAdapter, logicalDisplay);
                if (monitor.Controls.Count > 0)
                    result.Add(monitor);
            }
        }

        return result;
    }

    private void PopulateControls(MonitorInfo monitor, int adapterIndex, int displayIndex)
    {
        int caps = 0, valids = 0;
        if (AdlNative.ADL2_Display_ColorCaps_Get(_context, adapterIndex, displayIndex, ref caps, ref valids) != AdlNative.ADL_OK)
            return;

        foreach (int type in ControlTypes)
        {
            if ((caps & type) == 0)
                continue;

            int current = 0, def = 0, min = 0, max = 0, step = 0;
            if (AdlNative.ADL2_Display_Color_Get(_context, adapterIndex, displayIndex, type,
                    ref current, ref def, ref min, ref max, ref step) != AdlNative.ADL_OK)
                continue;

            if (max <= min)
                continue;

            monitor.Controls.Add(new ColorControl
            {
                Type = type,
                Name = ColorControl.DisplayName(type),
                Min = min,
                Max = max,
                Step = step <= 0 ? 1 : step,
                Default = def,
                Current = current,
                Unit = type == AdlNative.ADL_DISPLAY_COLOR_TEMPERATURE ? "K" : "",
            });
        }
    }

    public bool SetColor(MonitorInfo monitor, ColorControl control, int value)
    {
        if (!_initialized || monitor.BackendTag is not AdlRef r) return false;
        value = Math.Clamp(value, control.Min, control.Max);
        int rc = AdlNative.ADL2_Display_Color_Set(_context, r.Adapter, r.Display, control.Type, value);
        if (rc == AdlNative.ADL_OK)
            control.Current = value;
        return rc == AdlNative.ADL_OK;
    }

    private List<AdlNative.AdapterInfo> GetAdapterInfo(int numAdapters)
    {
        var list = new List<AdlNative.AdapterInfo>(numAdapters);
        int structSize = Marshal.SizeOf<AdlNative.AdapterInfo>();
        int bufferSize = structSize * numAdapters;
        IntPtr buffer = Marshal.AllocCoTaskMem(bufferSize);
        try
        {
            for (int i = 0; i < bufferSize; i++) Marshal.WriteByte(buffer, i, 0);
            for (int i = 0; i < numAdapters; i++)
                Marshal.WriteInt32(buffer + i * structSize, structSize);

            if (AdlNative.ADL2_Adapter_AdapterInfo_Get(_context, buffer, bufferSize) != AdlNative.ADL_OK)
                return list;

            for (int i = 0; i < numAdapters; i++)
                list.Add(Marshal.PtrToStructure<AdlNative.AdapterInfo>(buffer + i * structSize));
        }
        finally { Free(buffer); }
        return list;
    }

    private List<AdlNative.ADLDisplayInfo> GetDisplayInfo(int adapterIndex)
    {
        var list = new List<AdlNative.ADLDisplayInfo>();
        int numDisplays = 0;
        IntPtr arr = IntPtr.Zero;

        if (AdlNative.ADL2_Display_DisplayInfo_Get(_context, adapterIndex, ref numDisplays, ref arr, 0) != AdlNative.ADL_OK
            || arr == IntPtr.Zero || numDisplays <= 0)
        {
            if (arr != IntPtr.Zero) Free(arr);
            return list;
        }

        try
        {
            int structSize = Marshal.SizeOf<AdlNative.ADLDisplayInfo>();
            for (int i = 0; i < numDisplays; i++)
                list.Add(Marshal.PtrToStructure<AdlNative.ADLDisplayInfo>(arr + i * structSize));
        }
        finally { Free(arr); }
        return list;
    }

    public void Dispose()
    {
        if (_context != IntPtr.Zero)
        {
            try { AdlNative.ADL2_Main_Control_Destroy(_context); } catch { }
            _context = IntPtr.Zero;
        }
        _initialized = false;
    }
}

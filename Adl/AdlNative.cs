using System.Runtime.InteropServices;

namespace Luminosity.Adl;

/// <summary>
/// Raw P/Invoke surface for AMD's Display Library (atiadlxx.dll), which ships with the
/// AMD driver. We use the ADL2_* entry points (explicit context handle, thread-safe).
/// Only the handful of functions needed for per-display color control are declared.
/// </summary>
internal static class AdlNative
{
    private const string Dll = "atiadlxx.dll";

    public const int ADL_OK = 0;
    public const int ADL_MAX_PATH = 256;

    // Display info flags (ADLDisplayInfo.iDisplayInfoValue bitmask)
    public const int ADL_DISPLAY_DISPLAYINFO_DISPLAYCONNECTED = 0x00000001;
    public const int ADL_DISPLAY_DISPLAYINFO_DISPLAYMAPPED = 0x00000002;

    // Color control types (ADL_DISPLAY_COLOR_*)
    public const int ADL_DISPLAY_COLOR_BRIGHTNESS = 1;
    public const int ADL_DISPLAY_COLOR_CONTRAST = 2;
    public const int ADL_DISPLAY_COLOR_SATURATION = 4;
    public const int ADL_DISPLAY_COLOR_HUE = 8;
    public const int ADL_DISPLAY_COLOR_TEMPERATURE = 16;

    /// <summary>ADL requires the caller to supply a memory allocation routine.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr ADL_Main_Memory_Alloc(int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct AdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string UDID;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string PNPString;
        public int OSDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLDisplayID
    {
        public int DisplayLogicalIndex;
        public int DisplayPhysicalIndex;
        public int DisplayLogicalAdapterIndex;
        public int DisplayPhysicalAdapterIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct ADLDisplayInfo
    {
        public ADLDisplayID DisplayID;
        public int DisplayControllerIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string DisplayManufacturerName;
        public int DisplayType;
        public int DisplayOutputType;
        public int DisplayConnector;
        public int DisplayInfoMask;
        public int DisplayInfoValue;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters, ref IntPtr context);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Main_Control_Destroy(IntPtr context);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, ref int numAdapters);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int inputSize);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Display_DisplayInfo_Get(IntPtr context, int adapterIndex, ref int numDisplays, ref IntPtr displayInfoArray, int forceDetect);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Display_ColorCaps_Get(IntPtr context, int adapterIndex, int displayIndex, ref int caps, ref int valids);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Display_Color_Get(IntPtr context, int adapterIndex, int displayIndex, int colorType,
        ref int current, ref int defaultVal, ref int min, ref int max, ref int step);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Display_Color_Set(IntPtr context, int adapterIndex, int displayIndex, int colorType, int current);
}

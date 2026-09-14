using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace NBA.Capture.Windows.Interop;

/// <summary>
/// Bridges a raw Win32 HWND/HMONITOR to a WinRT <see cref="GraphicsCaptureItem"/>. This is the standard
/// (widely reproduced) interop shape for using Windows.Graphics.Capture from a Win32/.NET app instead of a
/// UWP app with a CoreWindow - see e.g. the community "Win32CaptureSample" project's Interop.cs, which this
/// follows. NOT verified end-to-end on this machine (no Windows host available) - see tasks.md.
/// </summary>
internal static class GraphicsCaptureItemInterop
{
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow([In] nint window, [In] ref Guid iid);

        nint CreateForMonitor([In] nint monitor, [In] ref Guid iid);
    }

    [DllImport("combase.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid,
        [Out, MarshalAs(UnmanagedType.IUnknown)] out object factory);

    private static IGraphicsCaptureItemInterop GetInteropFactory()
    {
        var interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
        RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref interopGuid, out var factory);
        return (IGraphicsCaptureItemInterop)factory;
    }

    internal static GraphicsCaptureItem CreateForWindow(nint hwnd)
    {
        var itemGuid = typeof(GraphicsCaptureItem).GUID;
        var itemPointer = GetInteropFactory().CreateForWindow(hwnd, ref itemGuid);
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    internal static GraphicsCaptureItem CreateForMonitor(nint hMonitor)
    {
        var itemGuid = typeof(GraphicsCaptureItem).GUID;
        var itemPointer = GetInteropFactory().CreateForMonitor(hMonitor, ref itemGuid);
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }
}

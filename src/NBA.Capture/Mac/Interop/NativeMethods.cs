using System.Runtime.InteropServices;

namespace NBA.Capture.Mac.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X;
    public double Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGSize
{
    public double Width;
    public double Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public CGPoint Origin;
    public CGSize Size;
}

/// <summary>
/// Raw P/Invoke surface against CoreGraphics/CoreFoundation - plain C APIs (no ObjC/Swift bridge needed),
/// validated against this Mac in a throwaway spike before being wired up here. See design.md's Mac capture
/// discussion: CGWindowListCreateImage/CGDisplayCreateImage produce real BGRA8 pixel buffers directly usable
/// as a <see cref="CapturedFrame"/>, and CGWindowListCopyWindowInfo enumerates capturable windows.
/// </summary>
internal static class NativeMethods
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal const uint KCFStringEncodingUTF8 = 0x08000100;
    internal const nint KCFNumberSInt32Type = 3;

    internal const uint KCGWindowListOptionOnScreenOnly = 1 << 0;
    internal const uint KCGWindowListExcludeDesktopElements = 1 << 4;
    internal const uint KCGWindowListOptionIncludingWindow = 1 << 3;
    internal const uint KCGWindowImageDefault = 0;
    internal const uint KCGWindowImageBoundsIgnoreFraming = 1 << 0;
    internal const uint KCGNullWindowId = 0;

    /// <summary>Passed to CGWindowListCreateImage to mean "use the window's own bounds", matching CGRectNull.</summary>
    internal static readonly CGRect CGRectNull = new()
    {
        Origin = new CGPoint { X = double.PositiveInfinity, Y = double.PositiveInfinity },
        Size = new CGSize { Width = 0, Height = 0 },
    };

    [DllImport(CoreGraphics)] internal static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] internal static extern IntPtr CGDisplayCreateImage(uint display);
    [DllImport(CoreGraphics)] internal static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] activeDisplays, out uint displayCount);
    [DllImport(CoreGraphics)] internal static extern CGRect CGDisplayBounds(uint display);

    [DllImport(CoreGraphics)] internal static extern nuint CGImageGetWidth(IntPtr image);
    [DllImport(CoreGraphics)] internal static extern nuint CGImageGetHeight(IntPtr image);
    [DllImport(CoreGraphics)] internal static extern nuint CGImageGetBytesPerRow(IntPtr image);
    [DllImport(CoreGraphics)] internal static extern IntPtr CGImageGetDataProvider(IntPtr image);
    [DllImport(CoreGraphics)] internal static extern IntPtr CGDataProviderCopyData(IntPtr provider);
    [DllImport(CoreGraphics)] internal static extern void CGImageRelease(IntPtr image);

    [DllImport(CoreGraphics)] internal static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)] internal static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, uint listOption, uint windowId, uint imageOption);
    [DllImport(CoreGraphics)] internal static extern byte CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);

    [DllImport(CoreFoundation)] internal static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundation)] internal static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundation)] internal static extern void CFRelease(IntPtr cf);
    [DllImport(CoreFoundation)] internal static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] internal static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint idx);
    [DllImport(CoreFoundation)] internal static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation, CharSet = CharSet.Ansi)] internal static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);
    [DllImport(CoreFoundation)] internal static extern byte CFStringGetCString(IntPtr theString, [Out] byte[] buffer, nint bufferSize, uint encoding);
    [DllImport(CoreFoundation)] internal static extern byte CFNumberGetValue(IntPtr number, nint theType, out int value);
}

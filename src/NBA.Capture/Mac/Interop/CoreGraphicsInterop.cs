using System.Runtime.InteropServices;
using System.Text;

namespace NBA.Capture.Mac.Interop;

/// <summary>
/// Higher-level helpers over <see cref="NativeMethods"/>: window/display enumeration and single-shot frame
/// capture, mirroring the role <c>WindowsCaptureSourceEnumerator</c>/interop classes play on the Windows side.
/// Unlike Windows.Graphics.Capture, none of this is a push-based stream - every call takes one snapshot, which
/// is why <see cref="MacFrameSource"/> polls on a timer instead of subscribing to a frame-arrived event.
/// </summary>
internal static class CoreGraphicsInterop
{
    /// <summary>Captures one frame for <paramref name="windowId"/> (a CGWindowID), or null if the window is gone or the buffer is unusable.</summary>
    internal static CapturedFrame? CaptureWindow(uint windowId)
    {
        var image = NativeMethods.CGWindowListCreateImage(
            NativeMethods.CGRectNull,
            NativeMethods.KCGWindowListOptionIncludingWindow,
            windowId,
            NativeMethods.KCGWindowImageDefault);

        return ConsumeImage(image);
    }

    /// <summary>Captures one frame for <paramref name="displayId"/> (a CGDirectDisplayID), or null if unusable.</summary>
    internal static CapturedFrame? CaptureDisplay(uint displayId)
    {
        var image = NativeMethods.CGDisplayCreateImage(displayId);
        return ConsumeImage(image);
    }

    /// <summary>
    /// Lists on-screen, normal-layer windows (skips menu bar extras/Dock/overlay chrome, which live on
    /// non-zero CGWindowLayer values) - the closest Mac equivalent of the Windows enumerator's "top-level
    /// app window" filter. Window titles (kCGWindowName) are frequently empty even for real app windows
    /// (unlike Win32's GetWindowText), so the owning app's name is the primary label, not the title.
    /// </summary>
    internal static IReadOnlyList<CaptureSourceDescriptor> EnumerateWindows()
    {
        var results = new List<CaptureSourceDescriptor>();

        var kWindowNumber = CfStr("kCGWindowNumber");
        var kOwnerName = CfStr("kCGWindowOwnerName");
        var kWindowName = CfStr("kCGWindowName");
        var kBounds = CfStr("kCGWindowBounds");
        var kLayer = CfStr("kCGWindowLayer");

        try
        {
            var listOption = NativeMethods.KCGWindowListOptionOnScreenOnly | NativeMethods.KCGWindowListExcludeDesktopElements;
            var windowList = NativeMethods.CGWindowListCopyWindowInfo(listOption, NativeMethods.KCGNullWindowId);
            if (windowList == IntPtr.Zero)
            {
                return results;
            }

            try
            {
                var count = NativeMethods.CFArrayGetCount(windowList);
                for (nint i = 0; i < count; i++)
                {
                    var dict = NativeMethods.CFArrayGetValueAtIndex(windowList, i);

                    NativeMethods.CFNumberGetValue(NativeMethods.CFDictionaryGetValue(dict, kLayer), NativeMethods.KCFNumberSInt32Type, out var layer);
                    if (layer != 0)
                    {
                        continue;
                    }

                    var owner = CfStrValue(NativeMethods.CFDictionaryGetValue(dict, kOwnerName));
                    if (string.IsNullOrWhiteSpace(owner))
                    {
                        continue;
                    }

                    NativeMethods.CGRectMakeWithDictionaryRepresentation(NativeMethods.CFDictionaryGetValue(dict, kBounds), out var bounds);
                    if (bounds.Size.Width < 100 || bounds.Size.Height < 100)
                    {
                        continue;
                    }

                    NativeMethods.CFNumberGetValue(NativeMethods.CFDictionaryGetValue(dict, kWindowNumber), NativeMethods.KCFNumberSInt32Type, out var windowId);
                    var title = CfStrValue(NativeMethods.CFDictionaryGetValue(dict, kWindowName));
                    var displayName = string.IsNullOrWhiteSpace(title) ? owner : $"{owner} - {title}";

                    results.Add(new CaptureSourceDescriptor(
                        Id: $"window:{windowId}",
                        DisplayName: displayName,
                        Kind: CaptureSourceKind.Window,
                        Handle: (nint)(uint)windowId,
                        ProcessName: owner));
                }
            }
            finally
            {
                NativeMethods.CFRelease(windowList);
            }
        }
        finally
        {
            NativeMethods.CFRelease(kWindowNumber);
            NativeMethods.CFRelease(kOwnerName);
            NativeMethods.CFRelease(kWindowName);
            NativeMethods.CFRelease(kBounds);
            NativeMethods.CFRelease(kLayer);
        }

        return results;
    }

    /// <summary>Lists connected displays via CGGetActiveDisplayList (the Mac equivalent of EnumDisplayMonitors).</summary>
    internal static IReadOnlyList<CaptureSourceDescriptor> EnumerateDisplays()
    {
        var results = new List<CaptureSourceDescriptor>();

        const uint maxDisplays = 16;
        var displays = new uint[maxDisplays];
        var error = NativeMethods.CGGetActiveDisplayList(maxDisplays, displays, out var count);
        if (error != 0)
        {
            return results;
        }

        var mainDisplay = NativeMethods.CGMainDisplayID();

        for (var i = 0; i < count; i++)
        {
            var displayId = displays[i];
            var bounds = NativeMethods.CGDisplayBounds(displayId);
            var isPrimary = displayId == mainDisplay;
            var displayName = $"Display {i + 1}{(isPrimary ? " (Primary)" : string.Empty)} - {(int)bounds.Size.Width}x{(int)bounds.Size.Height}";

            results.Add(new CaptureSourceDescriptor(
                Id: $"screen:{displayId}",
                DisplayName: displayName,
                Kind: CaptureSourceKind.Screen,
                Handle: (nint)displayId));
        }

        return results;
    }

    private static CapturedFrame? ConsumeImage(IntPtr image)
    {
        if (image == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var width = (int)NativeMethods.CGImageGetWidth(image);
            var height = (int)NativeMethods.CGImageGetHeight(image);
            var stride = (int)NativeMethods.CGImageGetBytesPerRow(image);
            if (width <= 0 || height <= 0 || stride <= 0)
            {
                return null;
            }

            var provider = NativeMethods.CGImageGetDataProvider(image);
            var cfData = NativeMethods.CGDataProviderCopyData(provider);
            if (cfData == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var expected = (long)stride * height;
                var length = (long)NativeMethods.CFDataGetLength(cfData);
                if (length < expected)
                {
                    return null;
                }

                var buffer = new byte[expected];
                Marshal.Copy(NativeMethods.CFDataGetBytePtr(cfData), buffer, 0, (int)expected);

                return new CapturedFrame
                {
                    Width = width,
                    Height = height,
                    Format = FramePixelFormat.Bgra8,
                    Stride = stride,
                    Pixels = buffer,
                    Timestamp = DateTimeOffset.UtcNow,
                };
            }
            finally
            {
                NativeMethods.CFRelease(cfData);
            }
        }
        finally
        {
            NativeMethods.CGImageRelease(image);
        }
    }

    private static IntPtr CfStr(string value) => NativeMethods.CFStringCreateWithCString(IntPtr.Zero, value, NativeMethods.KCFStringEncodingUTF8);

    private static string? CfStrValue(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero)
        {
            return null;
        }

        var buffer = new byte[1024];
        if (NativeMethods.CFStringGetCString(cfString, buffer, buffer.Length, NativeMethods.KCFStringEncodingUTF8) == 0)
        {
            return null;
        }

        var terminator = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, terminator >= 0 ? terminator : buffer.Length);
    }
}

using System.Text;
using NBA.Capture.Windows.Interop;

namespace NBA.Capture.Windows;

/// <summary>
/// Enumerates capturable top-level windows and connected monitors via classic Win32 user32 calls.
/// Windows.Graphics.Capture itself has no enumeration API - it only creates a GraphicsCaptureItem from an
/// HWND/HMONITOR you already have, which is why this uses plain user32 P/Invoke rather than WinRT.
/// </summary>
public sealed class WindowsCaptureSourceEnumerator : ICaptureSourceEnumerator
{
    public IReadOnlyList<CaptureSourceDescriptor> EnumerateSources()
    {
        var sources = new List<CaptureSourceDescriptor>();
        sources.AddRange(EnumerateWindows());
        sources.AddRange(EnumerateMonitors());
        return sources;
    }

    private static List<CaptureSourceDescriptor> EnumerateWindows()
    {
        var results = new List<CaptureSourceDescriptor>();
        var shellWindow = NativeMethods.GetShellWindow();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (hWnd == shellWindow)
            {
                return true;
            }

            if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
            {
                return true;
            }

            // Only include top-level windows a user would recognize as an app window, not tool windows/popups.
            if (NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOT) != hWnd)
            {
                return true;
            }

            var exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle.ToInt64() & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            var titleLength = NativeMethods.GetWindowTextLength(hWnd);
            if (titleLength == 0)
            {
                return true;
            }

            var titleBuilder = new StringBuilder(titleLength + 1);
            NativeMethods.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
            string? processName = null;
            try
            {
                processName = System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                // Process may have exited between enumeration and lookup, or access may be denied - the
                // window is still a valid capture source without a resolved process name.
            }

            results.Add(new CaptureSourceDescriptor(
                Id: $"window:{hWnd}",
                DisplayName: title,
                Kind: CaptureSourceKind.Window,
                Handle: hWnd,
                ProcessName: processName));

            return true;
        }, nint.Zero);

        return results;
    }

    private static List<CaptureSourceDescriptor> EnumerateMonitors()
    {
        var results = new List<CaptureSourceDescriptor>();
        var index = 0;

        NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, (hMonitor, _, ref rect, _) =>
        {
            index++;
            var info = new NativeMethods.MonitorInfoEx { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
            var displayName = $"Display {index}";
            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                var isPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                displayName = $"Display {index}{(isPrimary ? " (Primary)" : string.Empty)} - {rect.Width}x{rect.Height}";
            }

            results.Add(new CaptureSourceDescriptor(
                Id: $"screen:{hMonitor}",
                DisplayName: displayName,
                Kind: CaptureSourceKind.Screen,
                Handle: hMonitor));

            return true;
        }, nint.Zero);

        return results;
    }
}

using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace NBA.Capture.Windows.Interop;

/// <summary>
/// Bridges Vortice's Direct3D11/DXGI objects and the WinRT Direct3D11 interop types Windows.Graphics.Capture
/// requires (IDirect3DDevice for the frame pool, IDirect3DSurface -> ID3D11Texture2D for reading a captured
/// frame's pixels). Follows the same interop shape as the community "Win32CaptureSample" project's
/// Direct3D11Helpers.cs. NOT verified end-to-end on this machine (no Windows host available) - see tasks.md.
/// The two GUIDs obtained via `typeof(...).GUID` below are the specific detail most worth double-checking
/// against a real Windows build, since a wrong IID fails only at runtime (QueryInterface returns E_NOINTERFACE),
/// never at compile time.
/// </summary>
internal static class Direct3DInterop
{
    [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface([In] ref Guid iid);
    }

    /// <summary>Wraps a Vortice D3D11 device as the WinRT IDirect3DDevice Direct3D11CaptureFramePool.Create expects.</summary>
    internal static IDirect3DDevice CreateDirect3DDeviceFromD3D11Device(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var graphicsDevicePointer);
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevicePointer);
        }
        finally
        {
            Marshal.Release(graphicsDevicePointer);
        }
    }

    /// <summary>Unwraps a captured frame's WinRT surface back into the underlying Vortice D3D11 texture, for CPU readback.</summary>
    internal static ID3D11Texture2D GetD3D11Texture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = typeof(ID3D11Texture2D).GUID;
        var texturePointer = access.GetInterface(ref iid);
        try
        {
            return new ID3D11Texture2D(texturePointer);
        }
        finally
        {
            Marshal.Release(texturePointer);
        }
    }
}

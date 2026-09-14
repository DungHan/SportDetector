namespace NBA.Capture;

/// <summary>Pixel layout of a <see cref="CapturedFrame"/>'s buffer. Only BGRA8 is produced today.</summary>
public enum FramePixelFormat
{
    Bgra8,
}

/// <summary>
/// One captured frame, decoupled from any capture-backend type (Direct3D surface, WinRT SoftwareBitmap, ...) so
/// downstream code (NBA.Inference, NBA.App) never depends on Windows-specific types. The Windows.Graphics.Capture
/// backend copies its GPU surface into this CPU-readable buffer before handing it off.
/// </summary>
public sealed class CapturedFrame
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required FramePixelFormat Format { get; init; }

    /// <summary>Row stride in bytes. For <see cref="FramePixelFormat.Bgra8"/> this is at least <c>Width * 4</c>.</summary>
    public required int Stride { get; init; }

    /// <summary>Raw pixel bytes, row-major, <see cref="Stride"/> bytes per row.</summary>
    public required ReadOnlyMemory<byte> Pixels { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}

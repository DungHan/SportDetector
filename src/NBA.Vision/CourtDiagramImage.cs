namespace NBA.Vision;

/// <summary>
/// A rendered <see cref="CourtDiagramSpec"/> as raw BGRA8 pixels, matching <c>CapturedFrame</c>'s pixel layout
/// so the app layer can convert it to a displayable bitmap without depending on OpenCvSharp itself.
/// </summary>
public sealed record CourtDiagramImage(byte[] BgraPixels, int Width, int Height, int Stride);

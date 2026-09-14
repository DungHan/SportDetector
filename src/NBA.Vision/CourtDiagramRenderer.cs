using System.Runtime.InteropServices;
using OpenCvSharp;

namespace NBA.Vision;

/// <summary>
/// Draws a <see cref="CourtDiagramSpec"/> into a bitmap via OpenCvSharp. The output is pixel-for-pixel sized
/// at <paramref name="pixelsPerMeter"/> with no margin, so it lines up exactly with
/// <c>MinimapViewModel.CourtWidthPixels</c>/<c>CourtHeightPixels</c> (which use the same
/// meters-times-<c>PixelsPerMeter</c> math) and with marker positions plotted the same way.
/// </summary>
public static class CourtDiagramRenderer
{
    private const double DefaultLineThicknessMeters = 0.05;
    private const double SurroundBorderThicknessMeters = 0.15;

    public static CourtDiagramImage Render(CourtDiagramSpec spec, double pixelsPerMeter)
    {
        var width = Math.Max(1, (int)Math.Round(spec.SurfaceLengthMeters * pixelsPerMeter));
        var height = Math.Max(1, (int)Math.Round(spec.SurfaceWidthMeters * pixelsPerMeter));

        using var mat = new Mat(height, width, MatType.CV_8UC4, ParseColor(spec.Colors.SurfaceHex));

        if (spec.Colors.SurroundHex is { } surroundHex)
        {
            var borderThickness = ThicknessPx(SurroundBorderThicknessMeters, pixelsPerMeter);
            Cv2.Rectangle(mat, new Rect(0, 0, width, height), ParseColor(surroundHex), borderThickness);
        }

        var lineColor = ParseColor(spec.Colors.LineHex);

        // Every sport's surface dimensions are its true playing-boundary (sidelines/baselines, touchlines/goal
        // lines, etc.) - draw that boundary itself before the sport-specific markings, inset by half its own
        // thickness so the stroke stays fully on-canvas instead of being clipped by the image edge.
        var boundaryThickness = ThicknessPx(DefaultLineThicknessMeters, pixelsPerMeter);
        var inset = boundaryThickness / 2;
        Cv2.Rectangle(mat, new Rect(inset, inset, width - boundaryThickness, height - boundaryThickness), lineColor, boundaryThickness);

        foreach (var marking in spec.Markings)
        {
            Draw(mat, marking, lineColor, pixelsPerMeter);
        }

        return ToImage(mat, width, height);
    }

    private static void Draw(Mat mat, CourtMarking marking, Scalar lineColor, double pixelsPerMeter)
    {
        switch (marking)
        {
            case LineMarking line:
                Cv2.Line(mat, ToPoint(line.X1, line.Y1, pixelsPerMeter), ToPoint(line.X2, line.Y2, pixelsPerMeter),
                    lineColor, ThicknessPx(line.ThicknessMeters, pixelsPerMeter));
                break;

            case CircleMarking circle:
                Cv2.Circle(mat, ToPoint(circle.CenterX, circle.CenterY, pixelsPerMeter),
                    (int)Math.Round(circle.RadiusMeters * pixelsPerMeter), lineColor,
                    ThicknessPx(DefaultLineThicknessMeters, pixelsPerMeter));
                break;

            case ArcMarking arc:
                var radiusPx = (int)Math.Round(arc.RadiusMeters * pixelsPerMeter);
                Cv2.Ellipse(mat, ToPoint(arc.CenterX, arc.CenterY, pixelsPerMeter), new Size(radiusPx, radiusPx), 0,
                    arc.StartAngleDeg, arc.EndAngleDeg, lineColor, ThicknessPx(DefaultLineThicknessMeters, pixelsPerMeter));
                break;

            case RectMarking rect:
                var rectPx = new Rect(
                    (int)Math.Round(rect.X * pixelsPerMeter), (int)Math.Round(rect.Y * pixelsPerMeter),
                    (int)Math.Round(rect.Width * pixelsPerMeter), (int)Math.Round(rect.Height * pixelsPerMeter));
                var fillColor = rect.FillHex is { } fillHex ? ParseColor(fillHex) : lineColor;
                Cv2.Rectangle(mat, rectPx, rect.Filled ? fillColor : lineColor,
                    rect.Filled ? -1 : ThicknessPx(DefaultLineThicknessMeters, pixelsPerMeter));
                break;

            case TextMarking text:
                DrawText(mat, text, lineColor, pixelsPerMeter);
                break;
        }
    }

    /// <summary>
    /// HersheySimplex's unscaled cap-height is ~22px at <c>fontScale=1</c>/<c>thickness=1</c> - back-solve the
    /// scale from the marking's desired real-world height, then center the text on its point.
    /// </summary>
    private static void DrawText(Mat mat, TextMarking text, Scalar lineColor, double pixelsPerMeter)
    {
        const double UnscaledCapHeightPx = 22.0;
        var targetHeightPx = text.HeightMeters * pixelsPerMeter;
        var fontScale = targetHeightPx / UnscaledCapHeightPx;
        var thickness = Math.Max(1, (int)Math.Round(fontScale));

        var size = Cv2.GetTextSize(text.Text, HersheyFonts.HersheySimplex, fontScale, thickness, out _);
        var origin = ToPoint(text.CenterX, text.CenterY, pixelsPerMeter);
        var textOrigin = new Point(origin.X - (size.Width / 2), origin.Y + (size.Height / 2));

        Cv2.PutText(mat, text.Text, textOrigin, HersheyFonts.HersheySimplex, fontScale, lineColor, thickness, LineTypes.AntiAlias);
    }

    private static Point ToPoint(double xMeters, double yMeters, double pixelsPerMeter) =>
        new((int)Math.Round(xMeters * pixelsPerMeter), (int)Math.Round(yMeters * pixelsPerMeter));

    private static int ThicknessPx(double thicknessMeters, double pixelsPerMeter) =>
        Math.Max(1, (int)Math.Round(thicknessMeters * pixelsPerMeter));

    /// <summary>"#RRGGBB" to a BGRA <see cref="Scalar"/> - OpenCvSharp Mats store channels in BGR(A) order.</summary>
    private static Scalar ParseColor(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return new Scalar(b, g, r, 255);
    }

    private static CourtDiagramImage ToImage(Mat mat, int width, int height)
    {
        // A freshly allocated, unsliced Mat is always contiguous, so step == width * 4 here with no row padding.
        var stride = width * 4;
        var buffer = new byte[stride * height];
        Marshal.Copy(mat.Data, buffer, 0, buffer.Length);
        return new CourtDiagramImage(buffer, width, height, stride);
    }
}

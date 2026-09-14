namespace NBA.App.Models;

public enum OverlayShapeKind
{
    Point,
    Box,
    Polyline,
}

/// <summary>
/// Generic drawable annotation for the raw overlay view - a shape (point/box/polyline) + optional label/style,
/// deliberately independent of what produced it (court keypoints today; player boxes, jersey-number labels in
/// later phases). See design.md's "Raw view overlay: generic annotation model, not detection-type-specific".
/// Points are in image pixel space, matching the frame currently displayed.
/// </summary>
public sealed record OverlayAnnotation(
    OverlayShapeKind Shape,
    IReadOnlyList<(double X, double Y)> Points,
    string? Label = null,
    string? StyleKey = null)
{
    public static OverlayAnnotation ForPoint(double x, double y, string? label = null, string? styleKey = null) =>
        new(OverlayShapeKind.Point, [(x, y)], label, styleKey);

    public static OverlayAnnotation ForBox(double left, double top, double right, double bottom, string? label = null, string? styleKey = null) =>
        new(OverlayShapeKind.Box, [(left, top), (right, bottom)], label, styleKey);
}

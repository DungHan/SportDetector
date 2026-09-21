namespace NBA.App.Models;

/// <summary>
/// A flat, XAML-binding-friendly projection of one <see cref="OverlayAnnotation"/> (avoids binding directly
/// to a tuple list in markup, which Avalonia's compiled bindings handle poorly). <see cref="Width"/>/<see cref="Height"/>
/// are set only for a <see cref="OverlayShapeKind.Box"/> annotation.
/// </summary>
public sealed record AnnotationVisual(double X, double Y, double? Width, double? Height, string? Label, string? StyleKey = null, (byte R, byte G, byte B)? Color = null, double Opacity = 1.0)
{
    public static AnnotationVisual? FromAnnotation(OverlayAnnotation annotation) => annotation.Shape switch
    {
        OverlayShapeKind.Point when annotation.Points.Count >= 1 =>
            new AnnotationVisual(annotation.Points[0].X, annotation.Points[0].Y, null, null, annotation.Label, annotation.StyleKey),

        OverlayShapeKind.Box when annotation.Points.Count >= 2 =>
            new AnnotationVisual(
                annotation.Points[0].X,
                annotation.Points[0].Y,
                annotation.Points[1].X - annotation.Points[0].X,
                annotation.Points[1].Y - annotation.Points[0].Y,
                annotation.Label,
                annotation.StyleKey),

        // Polyline isn't produced by anything yet (no phase draws paths); once one does, render it via a
        // Polyline control bound to the raw annotation's Points instead of flattening it here.
        _ => null,
    };
}

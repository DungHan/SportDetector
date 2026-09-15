using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using NBA.App.Models;
using NBA.App.Services;
using NBA.Vision;

namespace NBA.App.ViewModels;

/// <summary>
/// The 2D court minimap: a standard court diagram for the current sport with projected points plotted on it,
/// plus a reserved status/info panel (score/state - not populated until a later phase, per the dual-view-shell
/// spec's "Reserved status/info panel within the minimap view"). Independently toggleable via <see cref="IsVisible"/>.
/// </summary>
public partial class MinimapViewModel : ViewModelBase
{
    private const string DefaultScorePlaceholder = "Score: not detected";

    private const double DefaultPixelsPerMeter = 20;

    /// <summary>
    /// Target on-screen height (px) for the diagram's <c>SurfaceWidthMeters</c> axis (always the vertical one -
    /// see <see cref="CourtHeightPixels"/>). A soccer pitch is roughly 13x a basketball court's area, so a
    /// single fixed meters-to-pixels scale would draw it ~4x larger in each dimension and blow the minimap
    /// panel's bounds; scaling per sport to a shared target height keeps every sport a similar on-screen size
    /// (and keeps line strokes - drawn a fixed thickness in meters - from shrinking to sub-pixel and vanishing).
    /// </summary>
    private const double TargetHeightPixels = 220;

    /// <summary>Current meters-to-pixels scale, recomputed per sport by <see cref="OnDiagramSpecChanged"/>. Markers use the same value so they stay aligned with the diagram under them.</summary>
    public double PixelsPerMeter { get; private set; } = DefaultPixelsPerMeter;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [ObservableProperty]
    public partial CourtGeometryDefinition? Geometry { get; set; }

    /// <summary>
    /// The current sport's court/field diagram to render, independent of <see cref="Geometry"/> - a sport can
    /// have a diagram (<see cref="CourtDiagramRegistry"/>) without supporting homography calibration yet.
    /// </summary>
    [ObservableProperty]
    public partial CourtDiagramSpec? DiagramSpec { get; set; }

    /// <summary>The rendered <see cref="DiagramSpec"/>, regenerated whenever it changes. See <see cref="OnDiagramSpecChanged"/>.</summary>
    [ObservableProperty]
    public partial WriteableBitmap? CourtDiagramBitmap { get; set; }

    [ObservableProperty]
    public partial bool HasValidCalibration { get; set; }

    public double CourtWidthPixels => ((DiagramSpec?.SurfaceLengthMeters ?? Geometry?.SurfaceLengthMeters) ?? 0) * PixelsPerMeter;

    public double CourtHeightPixels => ((DiagramSpec?.SurfaceWidthMeters ?? Geometry?.SurfaceWidthMeters) ?? 0) * PixelsPerMeter;

    public ObservableCollection<CourtMarker> Markers { get; } = [];

    /// <summary>Pixel-space projection of <see cref="Markers"/> for the view to bind against.</summary>
    public ObservableCollection<AnnotationVisual> MarkerVisuals { get; } = [];

    /// <summary>Placeholder-until-Phase-4 status text - the reserved info panel region. See design.md's "Minimap info panel: reserved region, placeholder content".</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = DefaultScorePlaceholder;

    partial void OnGeometryChanged(CourtGeometryDefinition? value)
    {
        OnPropertyChanged(nameof(CourtWidthPixels));
        OnPropertyChanged(nameof(CourtHeightPixels));
    }

    partial void OnDiagramSpecChanged(CourtDiagramSpec? value)
    {
        PixelsPerMeter = value is { SurfaceWidthMeters: > 0 } ? TargetHeightPixels / value.SurfaceWidthMeters : DefaultPixelsPerMeter;
        OnPropertyChanged(nameof(CourtWidthPixels));
        OnPropertyChanged(nameof(CourtHeightPixels));
        CourtDiagramBitmap = value is null
            ? null
            : FrameBitmapConverter.ToWriteableBitmap(CourtDiagramRenderer.Render(value, PixelsPerMeter));
    }

    public void SetMarkers(IEnumerable<CourtMarker> markers)
    {
        Markers.Clear();
        MarkerVisuals.Clear();
        foreach (var marker in markers)
        {
            Markers.Add(marker);
            MarkerVisuals.Add(new AnnotationVisual(marker.X * PixelsPerMeter, marker.Y * PixelsPerMeter, null, null, marker.Label, marker.StyleKey));
        }
    }

    public void SetStatusInfo(IReadOnlyDictionary<string, string>? statusInfo)
    {
        StatusText = statusInfo is { Count: > 0 }
            ? string.Join("  |  ", statusInfo.Select(kv => $"{kv.Key}: {kv.Value}"))
            : DefaultScorePlaceholder;
    }
}

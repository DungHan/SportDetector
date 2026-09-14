using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NBA.App.Models;
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

    /// <summary>Fixed scale for rendering court-space meters as pixels on the diagram.</summary>
    public const double PixelsPerMeter = 20;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [ObservableProperty]
    public partial CourtGeometryDefinition? Geometry { get; set; }

    [ObservableProperty]
    public partial bool HasValidCalibration { get; set; }

    public double CourtWidthPixels => (Geometry?.SurfaceLengthMeters ?? 0) * PixelsPerMeter;

    public double CourtHeightPixels => (Geometry?.SurfaceWidthMeters ?? 0) * PixelsPerMeter;

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

    public void SetMarkers(IEnumerable<CourtMarker> markers)
    {
        Markers.Clear();
        MarkerVisuals.Clear();
        foreach (var marker in markers)
        {
            Markers.Add(marker);
            MarkerVisuals.Add(new AnnotationVisual(marker.X * PixelsPerMeter, marker.Y * PixelsPerMeter, null, null, marker.Label));
        }
    }

    public void SetStatusInfo(IReadOnlyDictionary<string, string>? statusInfo)
    {
        StatusText = statusInfo is { Count: > 0 }
            ? string.Join("  |  ", statusInfo.Select(kv => $"{kv.Key}: {kv.Value}"))
            : DefaultScorePlaceholder;
    }
}

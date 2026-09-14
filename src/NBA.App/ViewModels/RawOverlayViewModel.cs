using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using NBA.App.Models;

namespace NBA.App.ViewModels;

/// <summary>
/// The raw view: the captured frame with a generic overlay of <see cref="OverlayAnnotation"/>s on top.
/// Independently toggleable via <see cref="IsVisible"/> - see the dual-view-shell spec's "Raw view with
/// extensible detection overlay, independently toggleable".
/// </summary>
public partial class RawOverlayViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [ObservableProperty]
    public partial WriteableBitmap? CurrentFrame { get; set; }

    public ObservableCollection<OverlayAnnotation> Annotations { get; } = [];

    /// <summary>Flattened projection of <see cref="Annotations"/> for the view to bind against - see <see cref="AnnotationVisual"/>.</summary>
    public ObservableCollection<AnnotationVisual> Visuals { get; } = [];

    public void SetAnnotations(IEnumerable<OverlayAnnotation> annotations)
    {
        Annotations.Clear();
        Visuals.Clear();
        foreach (var annotation in annotations)
        {
            Annotations.Add(annotation);
            if (AnnotationVisual.FromAnnotation(annotation) is { } visual)
            {
                Visuals.Add(visual);
            }
        }
    }
}

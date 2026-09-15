using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using NBA.App.Models;
using NBA.App.ViewModels;
using Path = Avalonia.Controls.Shapes.Path;

namespace NBA.App.Views;

public partial class RawOverlayView : UserControl
{
    private bool _visualsRebuildScheduled;

    public RawOverlayView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is RawOverlayViewModel viewModel)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                SyncOverlayCanvasSize(viewModel);
                viewModel.Visuals.CollectionChanged += (_, _) => ScheduleVisualsRebuild(viewModel);
                ScheduleVisualsRebuild(viewModel);
            }
        };
    }

    // SetAnnotations does Clear() followed by one Add() per annotation, so a single frame's update can raise
    // several CollectionChanged events back-to-back on the UI thread. Rebuilding on every one of them would
    // redo the same work N times per frame; coalescing to a single post per UI-thread tick makes it O(1) per
    // frame regardless of annotation count, and also sidesteps ever mutating/reading state mid-event.
    private void ScheduleVisualsRebuild(RawOverlayViewModel viewModel)
    {
        if (_visualsRebuildScheduled)
        {
            return;
        }

        _visualsRebuildScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _visualsRebuildScheduled = false;
            RebuildVisuals(viewModel);
        });
    }

    // Built by hand instead of an ItemsControl bound to Visuals: an ItemsControl with a Canvas ItemsPanel,
    // positioning each generated ContentPresenter via a Style-bound Canvas.Left/Top, produced presenters with
    // entirely correct Bounds/IsVisible/Opacity at every level (confirmed by dumping the live visual tree) that
    // nevertheless never painted a single pixel on screen - while plain, direct Canvas children (no
    // ItemsControl involved) painted fine. That pointed to an Avalonia 12.1.2 rendering bug specific to the
    // ItemsControl+ContentPresenter+Style combination, not to layout/visibility/coordinates (all of which were
    // independently verified correct). Managing shapes as direct Canvas children avoids that mechanism entirely.
    private void RebuildVisuals(RawOverlayViewModel viewModel)
    {
        OverlayCanvas.Children.Clear();

        foreach (var visual in viewModel.Visuals)
        {
            foreach (var control in CreateControls(visual))
            {
                OverlayCanvas.Children.Add(control);
            }
        }
    }

    private static IEnumerable<Control> CreateControls(AnnotationVisual visual)
    {
        var isKeypoint = visual.StyleKey == "keypoint";
        var isBox = visual.Width is not null;

        if (isKeypoint)
        {
            // Court keypoints get a bold X so they read clearly against the video.
            var path = new Path
            {
                Data = Geometry.Parse("M -8,-8 L 8,8 M 8,-8 L -8,8"),
                Stroke = Brushes.Red,
                StrokeThickness = 4,
                StrokeLineCap = PenLineCap.Round,
            };
            Canvas.SetLeft(path, visual.X);
            Canvas.SetTop(path, visual.Y);
            yield return path;
        }
        else if (isBox)
        {
            var rectangle = new Rectangle
            {
                Width = visual.Width!.Value,
                Height = visual.Height!.Value,
                Stroke = new SolidColorBrush(Color.Parse("#FF4040")),
                StrokeThickness = 2,
            };
            Canvas.SetLeft(rectangle, visual.X);
            Canvas.SetTop(rectangle, visual.Y);
            yield return rectangle;
        }
        else
        {
            // Plain point marker: every non-keypoint, non-box annotation.
            var ellipse = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Color.Parse("#FF4040")),
            };
            Canvas.SetLeft(ellipse, visual.X - 5);
            Canvas.SetTop(ellipse, visual.Y - 5);
            yield return ellipse;
        }

        if (visual.Label is { } label)
        {
            var text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 11,
            };
            Canvas.SetLeft(text, visual.X + 6);
            Canvas.SetTop(text, visual.Y - 16);
            yield return text;
        }
    }

    // Driven from code rather than a compiled binding path (CurrentFrame.PixelSize.Width/Height): the overlay
    // Canvas must be sized to the captured frame's native pixel dimensions so the Viewbox around it scales
    // annotation coordinates the same way Stretch="Uniform" scales the Image beneath it (see the .axaml
    // comment) - a nested binding path re-evaluating reliably every time CurrentFrame is replaced with a new
    // bitmap instance was less certain to get right than just reacting to the ViewModel's PropertyChanged.
    //
    // CurrentFrame is assigned from the frame-source's capture callback thread, not the UI thread, so this
    // handler runs there too - touching OverlayCanvas directly from it throws (Avalonia controls are
    // single-threaded) and, since that throw happens inside a native capture callback, silently kills further
    // frame delivery after the first frame. Always marshal the actual control update onto the UI thread.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RawOverlayViewModel.CurrentFrame) && sender is RawOverlayViewModel viewModel)
        {
            SyncOverlayCanvasSize(viewModel);
        }
    }

    private void SyncOverlayCanvasSize(RawOverlayViewModel viewModel)
    {
        if (viewModel.CurrentFrame is not { } frame)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            OverlayCanvas.Width = frame.PixelSize.Width;
            OverlayCanvas.Height = frame.PixelSize.Height;
        });
    }
}

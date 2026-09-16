using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NBA.App.Models;
using NBA.App.ViewModels;
using NBA.App.Views;
using Xunit;

namespace NBA.App.Tests;

public class RawOverlayViewSmokeTests
{
    [AvaloniaFact]
    public void View_ReflectsViewModelVisibility()
    {
        var viewModel = new RawOverlayViewModel();
        var view = new RawOverlayView { DataContext = viewModel };

        Assert.True(view.IsVisible);

        viewModel.IsVisible = false;

        Assert.False(view.IsVisible);
    }

    [AvaloniaFact]
    public void View_RendersOneMarkerPerAnnotation_AndUpdatesWhenTheyChange()
    {
        var viewModel = new RawOverlayViewModel();
        var view = new RawOverlayView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        viewModel.SetAnnotations(
        [
            OverlayAnnotation.ForPoint(10, 10, "A"),
            OverlayAnnotation.ForPoint(20, 20, "B"),
            OverlayAnnotation.ForPoint(30, 30, "C"),
        ]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var ellipses = view.GetVisualDescendants().OfType<Ellipse>().ToList();
        Assert.Equal(3, ellipses.Count);

        viewModel.SetAnnotations([OverlayAnnotation.ForPoint(1, 1, "Only")]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ellipses = view.GetVisualDescendants().OfType<Ellipse>().ToList();
        Assert.Single(ellipses);
    }

    [AvaloniaFact]
    public void View_RendersBoxAnnotationAsRectangle_NotEllipse()
    {
        var viewModel = new RawOverlayViewModel();
        var view = new RawOverlayView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        viewModel.SetAnnotations([OverlayAnnotation.ForBox(1, 2, 3, 4, "88%")]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var rectangles = view.GetVisualDescendants().OfType<Rectangle>().Where(r => r.IsVisible).ToList();
        var ellipses = view.GetVisualDescendants().OfType<Ellipse>().Where(e => e.IsVisible).ToList();
        Assert.Single(rectangles);
        Assert.Empty(ellipses);
    }

    [AvaloniaFact]
    public void View_RendersCrossedBoxAnnotation_WithoutThrowing()
    {
        // A tracked box's corners can momentarily cross (e.g. a motion-prediction glitch), making Width/Height
        // negative - Avalonia's Layoutable.Width/Height setters throw ArgumentException for negative values,
        // which crashed the whole app the first time a real (non-hardcoded) detection hit this case.
        var viewModel = new RawOverlayViewModel();
        var view = new RawOverlayView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        viewModel.SetAnnotations([OverlayAnnotation.ForBox(left: 100, top: 100, right: 50, bottom: 40, "crossed")]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var rectangles = view.GetVisualDescendants().OfType<Rectangle>().Where(r => r.IsVisible).ToList();
        var rectangle = Assert.Single(rectangles);
        Assert.Equal(50, rectangle.Width);
        Assert.Equal(60, rectangle.Height);
    }
}

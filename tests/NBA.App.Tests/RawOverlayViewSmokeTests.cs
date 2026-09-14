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
}

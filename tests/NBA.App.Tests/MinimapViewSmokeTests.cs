using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using NBA.App.Models;
using NBA.App.ViewModels;
using NBA.App.Views;
using NBA.Vision;
using Xunit;

namespace NBA.App.Tests;

public class MinimapViewSmokeTests
{
    [AvaloniaTheory]
    [InlineData("basketball")]
    [InlineData("soccer")]
    [InlineData("tennis")]
    [InlineData("american_football")]
    public void WithDiagramSpec_RendersCourtDiagramBitmap(string sportId)
    {
        Assert.True(CourtDiagramRegistry.TryGet(new SportType(sportId), out var spec));
        var viewModel = new MinimapViewModel { DiagramSpec = spec };
        var view = new MinimapView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        var image = view.GetVisualDescendants().OfType<Image>().FirstOrDefault();

        Assert.NotNull(image);
        Assert.NotNull(image!.Source);
        Assert.NotNull(viewModel.CourtDiagramBitmap);
    }

    [AvaloniaFact]
    public void WithoutValidCalibration_ShowsCalibratePrompt()
    {
        var viewModel = new MinimapViewModel { HasValidCalibration = false };
        var view = new MinimapView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        var prompt = view.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text != null && t.Text.Contains("Calibrate"));

        Assert.NotNull(prompt);
        Assert.True(prompt!.IsVisible);
    }

    [AvaloniaFact]
    public void WithValidCalibration_HidesCalibratePrompt()
    {
        var viewModel = new MinimapViewModel { HasValidCalibration = true };
        var view = new MinimapView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        var prompt = view.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text != null && t.Text.Contains("Calibrate"));

        Assert.NotNull(prompt);
        Assert.False(prompt!.IsVisible);
    }

    [AvaloniaFact]
    public void PlayerMarker_RendersWithDistinctFillFromKeypointMarker()
    {
        var viewModel = new MinimapViewModel { HasValidCalibration = true };
        var view = new MinimapView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        viewModel.SetMarkers([
            new CourtMarker(1, 1, "Landmark"),
            new CourtMarker(2, 2, "#7", "player"),
        ]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var fills = view.GetVisualDescendants().OfType<Ellipse>().Select(e => ((SolidColorBrush)e.Fill!).Color).ToList();

        Assert.Contains(Color.Parse("#40A0FF"), fills); // unstyled keypoint marker keeps its existing color
        Assert.Contains(Color.Parse("#F2F2F2"), fills); // "player"-styled marker gets a distinct color
    }

    [AvaloniaFact]
    public void PlayerMarker_LabelTextRendersOnMarker()
    {
        var viewModel = new MinimapViewModel { HasValidCalibration = true };
        var view = new MinimapView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        viewModel.SetMarkers([new CourtMarker(2, 2, "#7", "player")]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var label = view.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "#7");

        Assert.NotNull(label);
        Assert.True(label!.IsVisible);
    }

    // Regression guard for the Avalonia 12.1.2 rendering bug documented in MinimapView.axaml.cs/RawOverlayView.axaml.cs:
    // an ItemsControl+DataTemplate+Canvas.Left/Top marker produces a control with correct IsVisible/Bounds that still
    // never paints a pixel. Asserting IsVisible alone (as the two tests above do) would not catch a regression back to
    // that pattern, so this additionally asserts the marker is a *direct* child of the named markers Canvas.
    [AvaloniaFact]
    public void PlayerMarker_IsADirectCanvasChild_NotWrappedInAnItemsControlPresenter()
    {
        var viewModel = new MinimapViewModel { HasValidCalibration = true };
        var view = new MinimapView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        viewModel.SetMarkers([new CourtMarker(2, 2, "#7", "player")]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var canvas = view.GetVisualDescendants().OfType<Canvas>().First(c => c.Name == "MarkersCanvas");
        var ellipse = view.GetVisualDescendants().OfType<Ellipse>().First();

        Assert.Same(canvas, ellipse.GetVisualParent());
    }

    [AvaloniaFact]
    public void StatusPanel_ShowsPlaceholder_WhenNoStatusInfoConnected()
    {
        var viewModel = new MinimapViewModel();
        var view = new MinimapView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        Assert.Equal("Score: not detected", viewModel.StatusText);
    }

    [AvaloniaFact]
    public void StatusPanel_ShowsSuppliedText_WhenStatusInfoConnected()
    {
        var viewModel = new MinimapViewModel();
        viewModel.SetStatusInfo(new Dictionary<string, string> { ["Score"] = "58-52" });

        Assert.Contains("58-52", viewModel.StatusText);
    }
}

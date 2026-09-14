using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NBA.App.ViewModels;
using NBA.App.Views;
using Xunit;

namespace NBA.App.Tests;

public class MinimapViewSmokeTests
{
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

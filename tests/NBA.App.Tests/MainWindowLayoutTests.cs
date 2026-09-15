using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NBA.App.ViewModels;
using NBA.App.Views;
using NBA.Capture;
using NBA.Capture.Testing;
using NBA.OCR;
using NBA.Tracking;
using NBA.Vision;
using Xunit;

namespace NBA.App.Tests;

public class MainWindowLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"nba-profiles-{Guid.NewGuid():N}");

    private static readonly CaptureSourceDescriptor SourceA = new("a", "Source A", CaptureSourceKind.Window, 1);

    private MainWindowViewModel MakeViewModel()
    {
        var store = new FileSourceProfileStore(_directory);
        return new MainWindowViewModel(
            new FakeFrameSource(),
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new NullSportClassifier(), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new NullPlayerDetector(),
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new PlaybackRegionCoordinator(store));
    }

    [AvaloniaFact]
    public void RawOverlayView_IsDockedAboveMinimapView_ByDefault()
    {
        var window = new MainWindow { DataContext = MakeViewModel() };
        window.Show();

        var rawView = window.FindControl<RawOverlayView>("RawOverlayViewControl");
        var minimapView = window.FindControl<MinimapView>("MinimapViewControl");

        Assert.NotNull(rawView);
        Assert.NotNull(minimapView);
        Assert.True(Grid.GetRow(rawView!) < Grid.GetRow(minimapView!));
    }

    [AvaloniaFact]
    public void BothViews_VisibleByDefault_CanRunConcurrently()
    {
        var viewModel = MakeViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        Assert.True(viewModel.RawOverlay.IsVisible);
        Assert.True(viewModel.Minimap.IsVisible);

        var rawView = window.FindControl<RawOverlayView>("RawOverlayViewControl");
        var minimapView = window.FindControl<MinimapView>("MinimapViewControl");
        Assert.True(rawView!.IsVisible);
        Assert.True(minimapView!.IsVisible);
    }

    [AvaloniaFact]
    public void TogglingOneView_DoesNotAffectTheOther()
    {
        var viewModel = MakeViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        viewModel.RawOverlay.IsVisible = false;

        var rawView = window.FindControl<RawOverlayView>("RawOverlayViewControl");
        var minimapView = window.FindControl<MinimapView>("MinimapViewControl");
        Assert.False(rawView!.IsVisible);
        Assert.True(minimapView!.IsVisible); // unaffected by the raw view's toggle
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

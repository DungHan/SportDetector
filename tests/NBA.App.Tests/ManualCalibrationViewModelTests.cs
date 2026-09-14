using NBA.App.ViewModels;
using NBA.Vision;
using Xunit;

namespace NBA.App.Tests;

public class ManualCalibrationViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"nba-profiles-{Guid.NewGuid():N}");

    private static ImagePoint ToImage(CourtPoint court) => new((court.X * 10) + 100, (court.Y * 10) + 50);

    [Fact]
    public void Start_ActivatesWorkflow_AndPreselectsFirstLandmark()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));
        var viewModel = new ManualCalibrationViewModel(coordinator) { CurrentSport = SportType.Basketball };

        viewModel.StartCommand.Execute(null);

        Assert.True(viewModel.IsActive);
        Assert.NotNull(viewModel.SelectedLandmarkName);
    }

    [Fact]
    public void TryAddPointAtSelectedLandmark_AddsPoint_AndAdvancesToNextLandmark()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));
        var viewModel = new ManualCalibrationViewModel(coordinator) { CurrentSport = SportType.Basketball };
        viewModel.StartCommand.Execute(null);
        var firstLandmark = viewModel.SelectedLandmarkName;

        var added = viewModel.TryAddPointAtSelectedLandmark(new ImagePoint(10, 10));

        Assert.True(added);
        Assert.Single(viewModel.MarkedPoints);
        Assert.Equal(firstLandmark, viewModel.MarkedPoints[0].LandmarkName);
        Assert.DoesNotContain(firstLandmark, viewModel.RemainingLandmarkNames);
    }

    [Fact]
    public void Submit_WithFourKnownPoints_Succeeds_AndPersists()
    {
        var store = new FileSourceProfileStore(_directory);
        var coordinator = new CourtCalibrationCoordinator(store);
        var viewModel = new ManualCalibrationViewModel(coordinator)
        {
            CurrentSport = SportType.Basketball,
            SourceKey = "source-1",
        };
        viewModel.StartCommand.Execute(null);

        viewModel.AddPoint(ToImage(new CourtPoint(0, 0)), "BaselineCorner_Left_Near");
        viewModel.AddPoint(ToImage(new CourtPoint(28.6512, 0)), "BaselineCorner_Right_Near");
        viewModel.AddPoint(ToImage(new CourtPoint(0, 15.24)), "BaselineCorner_Left_Far");
        viewModel.AddPoint(ToImage(new CourtPoint(28.6512, 15.24)), "BaselineCorner_Right_Far");

        viewModel.SubmitCommand.Execute(null);

        Assert.Equal("Calibration succeeded.", viewModel.ResultMessage);
        Assert.False(viewModel.IsActive);
        Assert.NotNull(store.Load("source-1")?.Calibration);
    }

    [Fact]
    public void Submit_WithFewerThanFourPoints_Fails_AndStaysActive()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));
        var viewModel = new ManualCalibrationViewModel(coordinator)
        {
            CurrentSport = SportType.Basketball,
            SourceKey = "source-1",
        };
        viewModel.StartCommand.Execute(null);
        viewModel.AddPoint(new ImagePoint(1, 1), "BaselineCorner_Left_Near");

        viewModel.SubmitCommand.Execute(null);

        Assert.NotEqual("Calibration succeeded.", viewModel.ResultMessage);
        Assert.True(viewModel.IsActive);
    }

    [Fact]
    public void Cancel_ClearsMarkedPointsAndDeactivates()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));
        var viewModel = new ManualCalibrationViewModel(coordinator) { CurrentSport = SportType.Basketball };
        viewModel.StartCommand.Execute(null);
        viewModel.AddPoint(new ImagePoint(1, 1), "BaselineCorner_Left_Near");

        viewModel.CancelCommand.Execute(null);

        Assert.False(viewModel.IsActive);
        Assert.Empty(viewModel.MarkedPoints);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

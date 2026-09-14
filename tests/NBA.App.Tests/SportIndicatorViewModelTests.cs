using NBA.App.ViewModels;
using NBA.Vision;
using Xunit;

namespace NBA.App.Tests;

public class SportIndicatorViewModelTests
{
    [Fact]
    public void SelectableSports_PopulatedFromGeometryRegistry()
    {
        var viewModel = new SportIndicatorViewModel();

        Assert.Contains(SportType.Basketball, viewModel.SelectableSports);
    }

    [Fact]
    public void DisplayText_NoClassificationYet_SaysNotDetermined()
    {
        var viewModel = new SportIndicatorViewModel();

        Assert.Contains("not yet determined", viewModel.DisplayText);
    }

    [Fact]
    public void DisplayText_Unknown_SaysUnknown()
    {
        var viewModel = new SportIndicatorViewModel
        {
            Current = new SportClassification(SportType.Unknown, 0.2f, SportClassificationStatus.Unknown, false),
        };

        Assert.Contains("unknown", viewModel.DisplayText);
    }

    [Fact]
    public void DisplayText_RecognizedButUnsupported_SaysNotSupported()
    {
        var viewModel = new SportIndicatorViewModel
        {
            Current = new SportClassification(new SportType("soccer"), 0.9f, SportClassificationStatus.RecognizedButUnsupported, false),
        };

        Assert.Contains("not supported", viewModel.DisplayText);
    }

    [Fact]
    public void CanCalibrate_TrueOnlyWhenConfident()
    {
        var viewModel = new SportIndicatorViewModel();
        Assert.False(viewModel.CanCalibrate);

        viewModel.Current = new SportClassification(SportType.Unknown, 0f, SportClassificationStatus.Unknown, false);
        Assert.False(viewModel.CanCalibrate);

        viewModel.Current = new SportClassification(SportType.Basketball, 0.9f, SportClassificationStatus.Confident, false);
        Assert.True(viewModel.CanCalibrate);
    }
}

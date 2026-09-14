using NBA.App.ViewModels;
using NBA.Capture;
using NBA.Capture.Testing;
using Xunit;

namespace NBA.App.Tests;

public class SourcePickerViewModelTests
{
    private static readonly CaptureSourceDescriptor SourceA = new("a", "Source A", CaptureSourceKind.Window, 1);
    private static readonly CaptureSourceDescriptor SourceB = new("b", "Source B", CaptureSourceKind.Screen, 2);

    [Fact]
    public void Constructor_PopulatesSourcesAndSelectsFirst()
    {
        var viewModel = new SourcePickerViewModel(new FakeCaptureSourceEnumerator([SourceA, SourceB]));

        Assert.Equal([SourceA, SourceB], viewModel.Sources);
        Assert.Equal(SourceA, viewModel.SelectedSource);
    }

    [Fact]
    public void Refresh_KeepsCurrentSelection_WhenStillAvailable()
    {
        var viewModel = new SourcePickerViewModel(new FakeCaptureSourceEnumerator([SourceA, SourceB]));
        viewModel.SelectedSource = SourceB;

        viewModel.RefreshCommand.Execute(null);

        Assert.Equal(SourceB, viewModel.SelectedSource);
    }

    [Fact]
    public void Constructor_WithNoSources_LeavesSelectionNull()
    {
        var viewModel = new SourcePickerViewModel(new FakeCaptureSourceEnumerator([]));

        Assert.Empty(viewModel.Sources);
        Assert.Null(viewModel.SelectedSource);
    }
}

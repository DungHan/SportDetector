using System.ComponentModel;
using Avalonia.Controls;
using NBA.App.ViewModels;

namespace NBA.App.Views;

public partial class RawOverlayView : UserControl
{
    public RawOverlayView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is RawOverlayViewModel viewModel)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                SyncOverlayCanvasSize(viewModel);
            }
        };
    }

    // Driven from code rather than a compiled binding path (CurrentFrame.PixelSize.Width/Height): the overlay
    // Canvas must be sized to the captured frame's native pixel dimensions so the Viewbox around it scales
    // annotation coordinates the same way Stretch="Uniform" scales the Image beneath it (see the .axaml
    // comment) - a nested binding path re-evaluating reliably every time CurrentFrame is replaced with a new
    // bitmap instance was less certain to get right than just reacting to the ViewModel's PropertyChanged.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RawOverlayViewModel.CurrentFrame) && sender is RawOverlayViewModel viewModel)
        {
            SyncOverlayCanvasSize(viewModel);
        }
    }

    private void SyncOverlayCanvasSize(RawOverlayViewModel viewModel)
    {
        if (viewModel.CurrentFrame is { } frame)
        {
            OverlayCanvas.Width = frame.PixelSize.Width;
            OverlayCanvas.Height = frame.PixelSize.Height;
        }
    }
}

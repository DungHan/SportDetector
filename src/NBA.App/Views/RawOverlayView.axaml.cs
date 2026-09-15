using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
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

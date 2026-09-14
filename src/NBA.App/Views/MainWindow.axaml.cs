using Avalonia.Controls;
using Avalonia.Input;
using NBA.App.ViewModels;
using NBA.Vision;

namespace NBA.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Maps a click on the raw overlay view back to image-pixel coordinates and, if manual calibration is
    /// active, adds it as a point for the currently selected landmark. Reproduces the Stretch="Uniform"
    /// letterbox math (scale-to-fit, centered) so the mapping matches what's actually rendered.
    /// </summary>
    private void OnRawOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.ManualCalibration.IsActive)
        {
            return;
        }

        var frame = viewModel.RawOverlay.CurrentFrame;
        if (frame is null)
        {
            return;
        }

        var controlBounds = RawOverlayViewControl.Bounds;
        if (controlBounds.Width <= 0 || controlBounds.Height <= 0)
        {
            return;
        }

        var imageWidth = frame.PixelSize.Width;
        var imageHeight = frame.PixelSize.Height;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(controlBounds.Width / imageWidth, controlBounds.Height / imageHeight);
        var renderedWidth = imageWidth * scale;
        var renderedHeight = imageHeight * scale;
        var offsetX = (controlBounds.Width - renderedWidth) / 2;
        var offsetY = (controlBounds.Height - renderedHeight) / 2;

        var position = e.GetPosition(RawOverlayViewControl);
        var imageX = (position.X - offsetX) / scale;
        var imageY = (position.Y - offsetY) / scale;

        if (imageX < 0 || imageY < 0 || imageX > imageWidth || imageY > imageHeight)
        {
            return; // click landed in the letterbox margin, not on the image itself
        }

        viewModel.ManualCalibration.TryAddPointAtSelectedLandmark(new ImagePoint(imageX, imageY));
    }
}

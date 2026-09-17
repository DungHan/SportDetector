using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using NBA.App.Converters;
using NBA.App.ViewModels;

namespace NBA.App.Views;

public partial class MinimapView : UserControl
{
    private bool _markersRebuildScheduled;

    public MinimapView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MinimapViewModel viewModel)
            {
                viewModel.MarkerVisuals.CollectionChanged += (_, _) => ScheduleMarkersRebuild(viewModel);
                ScheduleMarkersRebuild(viewModel);
            }
        };
    }

    // SetMarkers does Clear() followed by one Add() per marker, so a single frame's update can raise several
    // CollectionChanged events back-to-back on the UI thread - coalesce to one rebuild per tick, same reasoning
    // as RawOverlayView.ScheduleVisualsRebuild.
    private void ScheduleMarkersRebuild(MinimapViewModel viewModel)
    {
        if (_markersRebuildScheduled)
        {
            return;
        }

        _markersRebuildScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _markersRebuildScheduled = false;
            RebuildMarkers(viewModel);
        });
    }

    // Built by hand instead of an ItemsControl bound to MarkerVisuals - see RawOverlayView.axaml.cs's
    // RebuildVisuals comment: that same ItemsControl+DataTemplate+Canvas.Left/Top pattern produced presenters
    // with entirely correct Bounds/IsVisible/Opacity that nevertheless never painted a single pixel (an
    // Avalonia 12.1.2 rendering bug specific to that combination). Direct Canvas children avoid it.
    private void RebuildMarkers(MinimapViewModel viewModel)
    {
        MarkersCanvas.Children.Clear();

        foreach (var visual in viewModel.MarkerVisuals)
        {
            var ellipse = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = (IBrush)MarkerStyleToBrushConverter.Instance.Convert(visual.StyleKey, typeof(IBrush), null, CultureInfo.InvariantCulture),
            };
            Canvas.SetLeft(ellipse, visual.X - 5);
            Canvas.SetTop(ellipse, visual.Y - 5);
            MarkersCanvas.Children.Add(ellipse);

            if (visual.Label is { } label)
            {
                var text = new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    Width = 24,
                    TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(text, visual.X - 12);
                Canvas.SetTop(text, visual.Y - 5);
                MarkersCanvas.Children.Add(text);
            }
        }
    }
}

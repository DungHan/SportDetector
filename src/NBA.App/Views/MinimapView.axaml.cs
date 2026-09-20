using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
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

    // Every marker on the minimap today is a tracked player (see MainWindowViewModel.ProcessFrameArrived -
    // court keypoints are no longer plotted here, just used to compute the calibration players project
    // through), drawn as a circle filled with that player's sampled jersey color and their number centered
    // inside it - not a flat-colored dot with the label floating beside it - so markers read at a glance as
    // "that player" instead of an anonymous dot plus a nearby caption.
    private const double MarkerDiameter = 20;

    // A track whose color estimate is still null (no successful match has carried a usable sampled color yet -
    // see TrackedPlayer.Color's doc comment) falls back to this neutral gray rather than an arbitrary color
    // that could be mistaken for a real jersey.
    private static readonly Color UnknownJerseyColor = Color.Parse("#9A9A9A");

    // Built by hand instead of an ItemsControl bound to MarkerVisuals - see RawOverlayView.axaml.cs's
    // RebuildVisuals comment: that same ItemsControl+DataTemplate+Canvas.Left/Top pattern produced presenters
    // with entirely correct Bounds/IsVisible/Opacity that nevertheless never painted a single pixel (an
    // Avalonia 12.1.2 rendering bug specific to that combination). Direct Canvas children avoid it.
    private void RebuildMarkers(MinimapViewModel viewModel)
    {
        MarkersCanvas.Children.Clear();

        foreach (var visual in viewModel.MarkerVisuals)
        {
            if (visual.StyleKey == "ball")
            {
                AddBallMarker(visual.X, visual.Y);
                continue;
            }

            var fillColor = visual.Color is { } rgb ? Color.FromRgb(rgb.R, rgb.G, rgb.B) : UnknownJerseyColor;
            var ellipse = new Ellipse
            {
                Width = MarkerDiameter,
                Height = MarkerDiameter,
                Fill = new SolidColorBrush(fillColor),
                Stroke = Brushes.Black,
                StrokeThickness = 1,
            };
            Canvas.SetLeft(ellipse, visual.X - (MarkerDiameter / 2));
            Canvas.SetTop(ellipse, visual.Y - (MarkerDiameter / 2));
            MarkersCanvas.Children.Add(ellipse);

            if (visual.Label is { } label)
            {
                var text = new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    Foreground = ReadableTextColorFor(fillColor),
                    Width = MarkerDiameter,
                    TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(text, visual.X - (MarkerDiameter / 2));
                Canvas.SetTop(text, visual.Y - 6); // Vertically centers a 9pt line within the circle.
                MarkersCanvas.Children.Add(text);
            }
        }
    }

    /// <summary>White text on a dark jersey color, black text on a light one, so the number stays legible against whatever color got sampled.</summary>
    private static IBrush ReadableTextColorFor(Color background)
    {
        var luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
        return luminance > 0.6 ? Brushes.Black : Brushes.White;
    }

    // Smaller than a player marker (MarkerDiameter) so it doesn't compete visually with the players it sits
    // among, and drawn with a basketball's seam pattern - one vertical seam, one horizontal seam, and two arcs
    // bulging away from the vertical seam - instead of a flat dot, so it reads as "the ball" at a glance.
    private const double BallDiameter = 12;
    private static readonly Color BallColor = Color.Parse("#F58426");
    private static readonly ISolidColorBrush BallFillBrush = new SolidColorBrush(BallColor);

    private void AddBallMarker(double x, double y)
    {
        var left = x - (BallDiameter / 2);
        var top = y - (BallDiameter / 2);

        var ellipse = new Ellipse
        {
            Width = BallDiameter,
            Height = BallDiameter,
            Fill = BallFillBrush,
            Stroke = Brushes.Black,
            StrokeThickness = 0.75,
        };
        Canvas.SetLeft(ellipse, left);
        Canvas.SetTop(ellipse, top);
        MarkersCanvas.Children.Add(ellipse);

        const double SeamThickness = 0.75;
        var pole1 = new Point(x, top);
        var pole2 = new Point(x, top + BallDiameter);
        var bulgeRadius = BallDiameter * 0.28;

        MarkersCanvas.Children.Add(new Line
        {
            StartPoint = pole1, EndPoint = pole2, Stroke = Brushes.Black, StrokeThickness = SeamThickness,
        });
        MarkersCanvas.Children.Add(new Line
        {
            StartPoint = new Point(left, y), EndPoint = new Point(left + BallDiameter, y),
            Stroke = Brushes.Black, StrokeThickness = SeamThickness,
        });
        MarkersCanvas.Children.Add(CreateSeamArc(pole1, pole2, bulgeRadius, SweepDirection.Clockwise, SeamThickness));
        MarkersCanvas.Children.Add(CreateSeamArc(pole1, pole2, bulgeRadius, SweepDirection.CounterClockwise, SeamThickness));
    }

    /// <summary>An arc from one pole of the ball to the other, bulging <paramref name="bulgeRadius"/> to one side of the vertical seam - one of the two curved seams of a basketball's texture.</summary>
    private static Avalonia.Controls.Shapes.Path CreateSeamArc(Point pole1, Point pole2, double bulgeRadius, SweepDirection sweep, double thickness)
    {
        var figure = new PathFigure { StartPoint = pole1, IsClosed = false };
        figure.Segments!.Add(new ArcSegment
        {
            Point = pole2,
            Size = new Size(bulgeRadius, Math.Abs(pole2.Y - pole1.Y) / 2),
            SweepDirection = sweep,
            IsLargeArc = false,
        });

        var geometry = new PathGeometry();
        geometry.Figures!.Add(figure);

        return new Avalonia.Controls.Shapes.Path { Data = geometry, Stroke = Brushes.Black, StrokeThickness = thickness };
    }
}

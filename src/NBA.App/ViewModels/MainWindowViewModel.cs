using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NBA.App.Models;
using NBA.App.Services;
using NBA.Capture;
using NBA.Vision;

namespace NBA.App.ViewModels;

/// <summary>
/// Orchestrates the whole shell: owns the capture→classify→calibrate→project pipeline and feeds every frame's
/// result to the raw overlay and minimap view models, which render independently and concurrently (dual-view-shell
/// spec's "Both views can run concurrently"). Also wires source switching to sport (re)classification and
/// calibration reuse/invalidation (spec's "Wire capture-source switching...").
/// </summary>
public sealed class MainWindowViewModel : IAsyncDisposable
{
    private readonly IFrameSource _frameSource;
    private readonly SportClassificationCoordinator _sportCoordinator;
    private readonly CourtCalibrationCoordinator _calibrationCoordinator;
    private readonly ICourtKeypointDetector _keypointDetector;
    private readonly IPlayerDetector _playerDetector;

    private string? _currentSourceKey;
    private bool _classifiedForCurrentSource;
    private readonly RelayCommand _reclassifyCommand;

    public MainWindowViewModel(
        IFrameSource frameSource,
        ICaptureSourceEnumerator sourceEnumerator,
        SportClassificationCoordinator sportCoordinator,
        CourtCalibrationCoordinator calibrationCoordinator,
        ICourtKeypointDetector keypointDetector,
        IPlayerDetector playerDetector)
    {
        _frameSource = frameSource;
        _sportCoordinator = sportCoordinator;
        _calibrationCoordinator = calibrationCoordinator;
        _keypointDetector = keypointDetector;
        _playerDetector = playerDetector;

        SourcePicker = new SourcePickerViewModel(sourceEnumerator);
        RawOverlay = new RawOverlayViewModel();
        Minimap = new MinimapViewModel();
        SportIndicator = new SportIndicatorViewModel();
        ManualCalibration = new ManualCalibrationViewModel(calibrationCoordinator);
        _reclassifyCommand = new RelayCommand(Reclassify, () => _currentSourceKey is not null);

        _frameSource.FrameArrived += OnFrameArrived;
        SourcePicker.PropertyChanged += OnSourcePickerPropertyChanged;

        if (SourcePicker.SelectedSource is { } initialSource)
        {
            _ = SelectSourceAsync(initialSource);
        }
    }

    /// <summary>
    /// Manually re-runs the automatic classifier against the *current* live frame, bypassing the cached
    /// per-source result - added after live testing showed classify-once-per-source-selection can go stale
    /// within the same window/tab: content playing inside an already-selected source (e.g. a YouTube video
    /// moving past its intro card into real gameplay) keeps whatever was classified at selection time until
    /// something forces a fresh look, and switching away and back doesn't help when the source key (kind +
    /// process + title) hasn't changed. <see cref="SportClassificationCoordinator.Reclassify"/> already existed
    /// for exactly this - it just had no UI control wired to it yet.
    /// </summary>
    public ICommand ReclassifyCommand => _reclassifyCommand;

    public SourcePickerViewModel SourcePicker { get; }

    public RawOverlayViewModel RawOverlay { get; }

    public MinimapViewModel Minimap { get; }

    public SportIndicatorViewModel SportIndicator { get; }

    public ManualCalibrationViewModel ManualCalibration { get; }

    private void OnSourcePickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourcePickerViewModel.SelectedSource) && SourcePicker.SelectedSource is { } source)
        {
            _ = SelectSourceAsync(source);
        }
    }

    private async Task SelectSourceAsync(CaptureSourceDescriptor source)
    {
        await _frameSource.StartAsync(source);

        _currentSourceKey = SourceIdentity.DeriveKey(source);
        _classifiedForCurrentSource = false;
        ManualCalibration.SourceKey = _currentSourceKey;

        RawOverlay.SetAnnotations([]);
        Minimap.SetMarkers([]);
        Minimap.HasValidCalibration = false;
        _reclassifyCommand.NotifyCanExecuteChanged();
    }

    private void Reclassify()
    {
        if (_currentSourceKey is not { } sourceKey || _frameSource.TryGetLatestFrame() is not { } frame)
        {
            return;
        }

        var classification = _sportCoordinator.Reclassify(
            sourceKey,
            new SportClassificationCoordinator.FrameSnapshot(frame.Pixels.ToArray(), frame.Width, frame.Height, frame.Stride));
        ApplyClassification(sourceKey, classification);
    }

    private void ApplyClassification(string sourceKey, SportClassification classification)
    {
        SportIndicator.Current = classification;

        // Offer reuse of a previously saved calibration for this exact source+sport pairing.
        var reused = _calibrationCoordinator.GetValidCalibration(sourceKey, classification.Sport);
        Minimap.HasValidCalibration = reused is not null;
        if (reused is not null && CourtGeometryRegistry.TryGet(classification.Sport, out var geometry))
        {
            Minimap.Geometry = geometry;
        }
    }

    private void OnFrameArrived(object? sender, FrameArrivedEventArgs e)
    {
        var frame = e.Frame;
        RawOverlay.CurrentFrame = FrameBitmapConverter.ToWriteableBitmap(frame);

        // Player detection is independent of sport/calibration state (vision/player-detection spec) - it runs
        // on every frame, unlike the sport-gated keypoint detection below. Marked at the box's bottom-center
        // (the player's feet) rather than as a box outline - this is also the point a later change would feed
        // into court-calibration's homography projection, since a player's court position is where they stand,
        // not their bounding box.
        var playerDetections = _playerDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
        var playerAnnotations = playerDetections.Select(d =>
            OverlayAnnotation.ForPoint((d.Left + d.Right) / 2, d.Bottom, $"{d.Confidence:P0}"));

        if (_currentSourceKey is not { } sourceKey)
        {
            RawOverlay.SetAnnotations(playerAnnotations);
            return;
        }

        if (!_classifiedForCurrentSource)
        {
            var classification = _sportCoordinator.ClassifyOrGetCached(
                sourceKey,
                () => new SportClassificationCoordinator.FrameSnapshot(frame.Pixels.ToArray(), frame.Width, frame.Height, frame.Stride));
            ApplyClassification(sourceKey, classification);
            _classifiedForCurrentSource = true;
        }

        var sport = SportIndicator.Current?.Sport;
        if (sport is null || SportIndicator.Current!.Status != SportClassificationStatus.Confident)
        {
            RawOverlay.SetAnnotations(playerAnnotations);
            return;
        }

        var keypoints = _keypointDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
        RawOverlay.SetAnnotations(playerAnnotations.Concat(keypoints.Select(k => OverlayAnnotation.ForPoint(k.Position.X, k.Position.Y, k.LandmarkName))));

        var calibration = _calibrationCoordinator.GetValidCalibration(sourceKey, sport.Value);
        if (calibration is null)
        {
            Minimap.HasValidCalibration = false;
            return;
        }

        Minimap.HasValidCalibration = true;
        if (CourtGeometryRegistry.TryGet(sport.Value, out var currentGeometry))
        {
            Minimap.Geometry = currentGeometry;
        }

        var markers = keypoints.Select(k =>
        {
            var court = PointProjector.Project(calibration, k.Position);
            return new CourtMarker(court.X, court.Y, k.LandmarkName);
        });
        Minimap.SetMarkers(markers);
    }

    public async ValueTask DisposeAsync()
    {
        _frameSource.FrameArrived -= OnFrameArrived;
        SourcePicker.PropertyChanged -= OnSourcePickerPropertyChanged;
        await _frameSource.DisposeAsync();
    }
}

using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using NBA.App.Models;
using NBA.App.Services;
using NBA.Capture;
using NBA.OCR;
using NBA.State;
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
    private readonly IScoreboardOcrEngine _scoreboardOcr;
    private readonly ISourceProfileStore _profileStore;
    private readonly GameStateTracker _gameStateTracker = new();

    private string? _currentSourceKey;
    private bool _classifiedForCurrentSource;
    private DateTimeOffset _lastScoreboardCheckAt;
    private readonly RelayCommand _reclassifyCommand;

    public MainWindowViewModel(
        IFrameSource frameSource,
        ICaptureSourceEnumerator sourceEnumerator,
        SportClassificationCoordinator sportCoordinator,
        CourtCalibrationCoordinator calibrationCoordinator,
        ICourtKeypointDetector keypointDetector,
        IPlayerDetector playerDetector,
        IScoreboardOcrEngine scoreboardOcr,
        ISourceProfileStore profileStore)
    {
        _frameSource = frameSource;
        _sportCoordinator = sportCoordinator;
        _calibrationCoordinator = calibrationCoordinator;
        _keypointDetector = keypointDetector;
        _playerDetector = playerDetector;
        _scoreboardOcr = scoreboardOcr;
        _profileStore = profileStore;

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

        // The diagram itself only depends on the classified sport, not on calibration - draw it as soon as a
        // sport is known so the minimap shows the right court/field even before any calibration exists.
        Minimap.DiagramSpec = CourtDiagramRegistry.TryGet(classification.Sport, out var diagram) ? diagram : null;

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
        var bitmap = FrameBitmapConverter.ToWriteableBitmap(frame);

        // Player detection is independent of sport/calibration state (vision/player-detection spec) - it runs
        // on every frame, unlike the sport-gated keypoint detection below. Guarded because an inference
        // failure on one frame (e.g. an unsupported ONNX op on this machine's runtime build) must not take
        // down the capture loop that calls this handler - see MacFrameSource.PollLoopAsync, which has no
        // catch-all of its own around FrameArrived subscribers.
        IReadOnlyList<PlayerDetection> playerDetections;
        try
        {
            playerDetections = _playerDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[player-detect] detection failed for this frame, treating as none: {ex.Message}");
            playerDetections = [];
        }

        var playerAnnotations = playerDetections
            .Select(d => OverlayAnnotation.ForBox(d.Left, d.Top, d.Right, d.Bottom, $"{d.Confidence:P0}"))
            .ToList();

        // This handler runs synchronously on MacFrameSource's background polling thread, not the UI thread
        // (macOS capture has no push-based callback - see MacFrameSource.PollLoopAsync). Everything above is
        // a pure computation; everything below mutates state Avalonia's UI renders from. In particular,
        // RawOverlay.SetAnnotations/Minimap.SetMarkers mutate ObservableCollections that the UI thread's
        // ItemsControl may be enumerating at the same moment to render, which throws ("Collection was
        // modified; enumeration operation may not execute") instead of merely glitching - so every mutation
        // from here on is marshaled onto the UI thread via Dispatcher.UIThread.Post.
        if (_currentSourceKey is not { } sourceKey)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RawOverlay.CurrentFrame = bitmap;
                RawOverlay.SetAnnotations(playerAnnotations);
            });
            return;
        }

        // Scoreboard OCR is throttled to ~1Hz (unlike player/keypoint detection above, which run every frame) -
        // it's comparatively expensive (a subprocess call on macOS - see MacVisionOcrEngine) and the scoreboard
        // doesn't change fast enough to need per-frame updates. Guarded the same way as the detectors above so
        // an OCR failure on one frame can't take down the capture loop.
        if (frame.Timestamp - _lastScoreboardCheckAt >= TimeSpan.FromSeconds(1))
        {
            _lastScoreboardCheckAt = frame.Timestamp;
            try
            {
                var region = _profileStore.Load(sourceKey)?.ScoreboardRegion ?? NormalizedRect.DefaultScoreboardRegion;
                var crop = FrameCropper.Crop(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride, region.X, region.Y, region.Width, region.Height);
                var lines = _scoreboardOcr.Recognize(crop.Pixels, crop.Width, crop.Height, crop.Stride);
                var reading = ScoreboardTextParser.Parse(lines, frame.Timestamp);
                _gameStateTracker.Update(reading);
                var statusInfo = _gameStateTracker.ToStatusDictionary();
                Dispatcher.UIThread.Post(() => Minimap.SetStatusInfo(statusInfo));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[scoreboard-ocr] recognition failed for this frame, keeping last known state: {ex.Message}");
            }
        }

        if (!_classifiedForCurrentSource)
        {
            var classification = _sportCoordinator.ClassifyOrGetCached(
                sourceKey,
                () => new SportClassificationCoordinator.FrameSnapshot(frame.Pixels.ToArray(), frame.Width, frame.Height, frame.Stride));
            _classifiedForCurrentSource = true;

            // Synchronous, unlike the mutations below - ApplyClassification only ever assigns plain properties
            // (SportIndicator.Current, Minimap.DiagramSpec/HasValidCalibration/Geometry), never touches an
            // ObservableCollection, so it doesn't have the "enumerated while mutated" hazard those do. It must
            // run before the `sport` read directly below, in this same call, not on a future dispatcher tick.
            ApplyClassification(sourceKey, classification);
        }

        var sport = SportIndicator.Current?.Sport;
        if (sport is null || SportIndicator.Current!.Status != SportClassificationStatus.Confident)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RawOverlay.CurrentFrame = bitmap;
                RawOverlay.SetAnnotations(playerAnnotations);
            });
            return;
        }

        IReadOnlyList<DetectedKeypoint> keypoints;
        try
        {
            keypoints = _keypointDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[keypoint-detect] detection failed for this frame, treating as none: {ex.Message}");
            keypoints = [];
        }

        var keypointAnnotations = keypoints
            .Select(k => OverlayAnnotation.ForPoint(k.Position.X, k.Position.Y, k.LandmarkName))
            .ToList();

        var calibration = _calibrationCoordinator.GetValidCalibration(sourceKey, sport.Value);
        CourtGeometryDefinition? currentGeometry = null;
        List<CourtMarker>? markers = null;
        if (calibration is not null)
        {
            CourtGeometryRegistry.TryGet(sport.Value, out currentGeometry);
            markers = keypoints.Select(k =>
            {
                var court = PointProjector.Project(calibration, k.Position);
                return new CourtMarker(court.X, court.Y, k.LandmarkName);
            }).ToList();
        }

        Dispatcher.UIThread.Post(() =>
        {
            RawOverlay.CurrentFrame = bitmap;
            RawOverlay.SetAnnotations(playerAnnotations.Concat(keypointAnnotations));

            Minimap.HasValidCalibration = calibration is not null;
            if (currentGeometry is not null)
            {
                Minimap.Geometry = currentGeometry;
            }
            if (markers is not null)
            {
                Minimap.SetMarkers(markers);
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _frameSource.FrameArrived -= OnFrameArrived;
        SourcePicker.PropertyChanged -= OnSourcePickerPropertyChanged;
        await _frameSource.DisposeAsync();
    }
}

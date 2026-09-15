using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using NBA.App.Models;
using NBA.App.Services;
using NBA.Capture;
using NBA.OCR;
using NBA.State;
using NBA.Tracking;
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
    private readonly IPlayerTracker _playerTracker;
    private readonly IScoreboardOcrEngine _scoreboardOcr;
    private readonly ISourceProfileStore _profileStore;
    private readonly PlaybackRegionCoordinator _playbackRegionCoordinator;
    private readonly GameStateTracker _gameStateTracker = new();

    private static readonly TimeSpan KeypointDetectionInterval = TimeSpan.FromMilliseconds(150);

    private string? _currentSourceKey;
    private bool _classifiedForCurrentSource;
    private NormalizedRect? _currentPlaybackRegion;
    private DateTimeOffset _lastScoreboardCheckAt;
    private DateTimeOffset _lastKeypointCheckAt;
    private IReadOnlyList<DetectedKeypoint> _lastKeypoints = [];
    private readonly RelayCommand _reclassifyCommand;
    private readonly int _detectionIntervalFrames;
    private int _frameCounter;

    public MainWindowViewModel(
        IFrameSource frameSource,
        ICaptureSourceEnumerator sourceEnumerator,
        SportClassificationCoordinator sportCoordinator,
        CourtCalibrationCoordinator calibrationCoordinator,
        ICourtKeypointDetector keypointDetector,
        IPlayerDetector playerDetector,
        IPlayerTracker playerTracker,
        IScoreboardOcrEngine scoreboardOcr,
        ISourceProfileStore profileStore,
        PlaybackRegionCoordinator playbackRegionCoordinator,
        int detectionIntervalFrames = 3)
    {
        _frameSource = frameSource;
        _sportCoordinator = sportCoordinator;
        _calibrationCoordinator = calibrationCoordinator;
        _keypointDetector = keypointDetector;
        _playerDetector = playerDetector;
        _playerTracker = playerTracker;
        _scoreboardOcr = scoreboardOcr;
        _profileStore = profileStore;
        _playbackRegionCoordinator = playbackRegionCoordinator;
        _detectionIntervalFrames = detectionIntervalFrames;

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

        // Reuse a region detected for this exact source in an earlier session (mirrors calibration/scoreboard
        // reuse below); null just means "not detected yet" - accumulation resumes on this source's own frames.
        _currentPlaybackRegion = _playbackRegionCoordinator.GetPersistedRegion(_currentSourceKey);
        ManualCalibration.SourceKey = _currentSourceKey;

        // Track IDs are only meaningful within one continuous view of a source - an unrelated source switch
        // must not carry stale identities into a scene the tracker never saw (tracking/player-tracking spec).
        _playerTracker.Reset();

        // Reset alongside the tracker so a fresh source's first frame is always a real detection frame (index 0),
        // not partway through a stale cadence cycle left over from the previous source.
        _frameCounter = 0;

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

    private CroppedFrame? CropToPlaybackRegion(CapturedFrame frame) =>
        _currentPlaybackRegion is { } region
            ? FrameCropper.Crop(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride, region.X, region.Y, region.Width, region.Height)
            : null;

    private static PlayerDetection OffsetToFullFrame(PlayerDetection detection, int left, int top) =>
        new(detection.Left + left, detection.Top + top, detection.Right + left, detection.Bottom + top, detection.Confidence);

    private static DetectedKeypoint OffsetToFullFrame(DetectedKeypoint keypoint, int left, int top) =>
        keypoint with { Position = new ImagePoint(keypoint.Position.X + left, keypoint.Position.Y + top) };

    private void OnFrameArrived(object? sender, FrameArrivedEventArgs e)
    {
        var frame = e.Frame;
        var bitmap = FrameBitmapConverter.ToWriteableBitmap(frame);

        // Auto-detects the sub-rectangle that's actually gameplay versus surrounding page chrome (YouTube
        // comments, recommended-video thumbnails, ...) that happens to sit inside the captured frame - once
        // found (and persisted per source), it stays cached in _currentPlaybackRegion and this stops running.
        if (_currentPlaybackRegion is null && _currentSourceKey is { } accumulatingSourceKey)
        {
            _currentPlaybackRegion = _playbackRegionCoordinator.Accumulate(accumulatingSourceKey, frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
        }

        // Cropping detector input to the playback region (once known) keeps player/keypoint detection from
        // running inference over that surrounding chrome. Detector output comes back in the crop's own pixel
        // space, so results are offset back into full-frame coordinates immediately below (see OffsetToFullFrame),
        // before anything downstream (tracker, overlay, court projection) ever sees them - none of that code
        // needs to know cropping happened.
        var playbackCrop = CropToPlaybackRegion(frame);

        // Player detection is independent of sport/calibration state (vision/player-detection spec), but only
        // runs every `_detectionIntervalFrames`th captured frame - it's the most expensive step in this pipeline,
        // and the tracker's own motion model (PredictOnly, below) can carry a track's position between real
        // detections. Guarded because an inference failure on one frame (e.g. an unsupported ONNX op on this
        // machine's runtime build) must not take down the capture loop that calls this handler - see
        // MacFrameSource.PollLoopAsync, which has no catch-all of its own around FrameArrived subscribers.
        var isDetectionFrame = _frameCounter % _detectionIntervalFrames == 0;
        _frameCounter++;

        IReadOnlyList<TrackedPlayer> trackedPlayers;
        if (isDetectionFrame)
        {
            IReadOnlyList<PlayerDetection> playerDetections;
            try
            {
                playerDetections = playbackCrop is { } crop
                    ? _playerDetector.Detect(crop.Pixels, crop.Width, crop.Height, crop.Stride)
                        .Select(d => OffsetToFullFrame(d, crop.Left, crop.Top))
                        .ToList()
                    : _playerDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[player-detect] detection failed for this frame, treating as none: {ex.Message}");
                playerDetections = [];
            }

            // Detections are fed through the tracker to attach a stable track ID per player (tracking/player-tracking
            // spec) before rendering, so the same physical player keeps the same box/ID across frames instead of an
            // unlabeled per-frame foot-point.
            trackedPlayers = _playerTracker.Update(playerDetections);
        }
        else
        {
            // No detection was attempted this frame - advance motion prediction only, so tracked boxes keep
            // moving smoothly instead of freezing, without counting this frame against any track's occlusion
            // buffer (tracking/player-tracking spec's "Advance motion prediction without detection").
            trackedPlayers = _playerTracker.PredictOnly();
        }

        var playerAnnotations = trackedPlayers
            .Select(t => OverlayAnnotation.ForBox(t.Left, t.Top, t.Right, t.Bottom, $"#{t.TrackId}"))
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

        // Scoreboard OCR is throttled to ~1Hz (a coarser, wall-clock-based throttle than player detection's
        // frame-count cadence above) - it's comparatively expensive (a subprocess call on macOS - see
        // MacVisionOcrEngine) and the scoreboard doesn't change fast enough to need per-frame updates. Guarded
        // the same way as the detectors above so an OCR failure on one frame can't take down the capture loop.
        if (frame.Timestamp - _lastScoreboardCheckAt >= TimeSpan.FromSeconds(1))
        {
            _lastScoreboardCheckAt = frame.Timestamp;
            try
            {
                var region = _profileStore.Load(sourceKey)?.ScoreboardRegion ?? NormalizedRect.DefaultScoreboardRegion;

                // The scoreboard is part of the broadcast itself, so once the playback region is known, position
                // it relative to that region instead of the full captured frame - region (e.g. the default's
                // bottom-15%-of-full-width) assumes the game fills the frame, which is wrong once surrounding
                // page chrome (YouTube UI, ...) is also inside it. playbackCrop is the same crop already computed
                // above for player/keypoint detection.
                var crop = playbackCrop is { } gameCrop
                    ? FrameCropper.Crop(gameCrop.Pixels, gameCrop.Width, gameCrop.Height, gameCrop.Stride, region.X, region.Y, region.Width, region.Height)
                    : FrameCropper.Crop(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride, region.X, region.Y, region.Width, region.Height);
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

        // Keypoint detection is throttled to ~6.7Hz (every 150ms) via a wall-clock timer, a separate cadence
        // mechanism from player detection's frame-count-based one above - the court's keypoints only move when
        // the camera pans/zooms/cuts, so re-running the ONNX inference on every single frame is wasted work.
        // Between checks, the last detected keypoints are reused so the overlay/minimap don't blank out on
        // skipped frames.
        if (frame.Timestamp - _lastKeypointCheckAt >= KeypointDetectionInterval)
        {
            _lastKeypointCheckAt = frame.Timestamp;
            try
            {
                _lastKeypoints = playbackCrop is { } crop
                    ? _keypointDetector.Detect(crop.Pixels, crop.Width, crop.Height, crop.Stride)
                        .Select(k => OffsetToFullFrame(k, crop.Left, crop.Top))
                        .ToList()
                    : _keypointDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[keypoint-detect] detection failed for this frame, treating as none: {ex.Message}");
                _lastKeypoints = [];
            }
        }

        var keypoints = _lastKeypoints;
        var keypointAnnotations = keypoints
            .Select(k => OverlayAnnotation.ForPoint(k.Position.X, k.Position.Y, k.LandmarkName, "keypoint"))
            .ToList();

        var calibration = _calibrationCoordinator.GetValidCalibration(sourceKey, sport.Value);
        CourtGeometryDefinition? currentGeometry = null;
        List<CourtMarker>? markers = null;
        if (calibration is not null)
        {
            CourtGeometryRegistry.TryGet(sport.Value, out currentGeometry);

            var keypointMarkers = keypoints.Select(k =>
            {
                var court = PointProjector.Project(calibration, k.Position);
                return new CourtMarker(court.X, court.Y, k.LandmarkName);
            });

            // Foot point (bottom-center of the box) rather than the box itself - the minimap plots a single
            // court-space position per player, not an area (dual-view-shell spec's "Minimap plots tracked
            // players' court positions"). Styled "player" so MinimapView.axaml renders it distinctly from the
            // unstyled keypoint markers above, now that both appear on the same diagram.
            var playerMarkers = trackedPlayers.Select(t =>
            {
                var footPoint = new ImagePoint((t.Left + t.Right) / 2, t.Bottom);
                var court = PointProjector.Project(calibration, footPoint);
                return new CourtMarker(court.X, court.Y, $"#{t.TrackId}", "player");
            });

            markers = keypointMarkers.Concat(playerMarkers).ToList();
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

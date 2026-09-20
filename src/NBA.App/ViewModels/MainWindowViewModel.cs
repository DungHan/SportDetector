using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using NBA.App.Models;
using NBA.App.Services;
using NBA.Capture;
using NBA.JerseyOcr;
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
    private readonly IMultiClassObjectDetector _multiClassObjectDetector;
    private readonly IPlayerTracker _playerTracker;
    private readonly IMultiClassObjectDetector _ballDetector;
    private readonly IScoreboardOcrEngine _scoreboardOcr;
    private readonly ISourceProfileStore _profileStore;
    private readonly IJerseyNumberRecognizer _jerseyNumberRecognizer;
    private readonly IJerseyNumberVoteAggregator _jerseyNumberVoteAggregator;
    private readonly PlaybackRegionCoordinator _playbackRegionCoordinator;
    private readonly GameStateTracker _gameStateTracker = new();

    private static readonly TimeSpan KeypointDetectionInterval = TimeSpan.FromMilliseconds(150);

    // Sport classification is retried at this cadence (not per-frame - it's a real inference call) for as long
    // as it keeps coming back Unknown, e.g. because the opening frames of a source don't show the court yet.
    private static readonly TimeSpan SportClassificationRetryInterval = TimeSpan.FromMilliseconds(500);

    // The on-court-object-detection classes whose union bounding box sources the scoreboard OCR crop region
    // (ocr/scoreboard-recognition spec) - deliberately excludes Ball/Hoop/Player/Ref, which aren't part of the
    // scoreboard graphic. Neither of today's two trained models (_multiClassObjectDetector's Player/Ref-only
    // export, _ballDetector's Ball-only export) emits any of these classes at all, so scoreboardCandidates
    // below is always empty for now - this degrades to NormalizedRect.DefaultScoreboardRegion/the per-source
    // manual override, the same fallback already used when no scoreboard detection has happened yet, rather
    // than throwing or breaking scoreboard OCR outright. Kept (not deleted) so a future model retrain that
    // reintroduces scoreboard-element classes lights this back up with no code changes.
    private static readonly HashSet<string> ScoreboardRelatedClassNames =
    [
        "Period", "Shot Clock", "Team Name", "Team Points", "Time Remaining",
    ];

    // Basketball never has more than 10 players (5 per team) on court at once. ByteTrackPlayerTracker
    // deliberately keeps a track alive and visible for a few frames after it stops matching real detections
    // (see its type-level doc comment's occlusion-buffer rationale), so a live track count above 10 means some
    // of them are coasting duplicates of a player whose track briefly fragmented, not real extra people - the
    // minimap caps to this many, preferring tracks with a real match this frame over ones merely coasting.
    private const int MaxPlayersOnCourtSimultaneously = 10;

    private string? _currentSourceKey;
    private NormalizedRect? _currentPlaybackRegion;
    private DateTimeOffset _lastScoreboardCheckAt;
    private DateTimeOffset _lastKeypointCheckAt;
    private DateTimeOffset _lastSportClassificationCheckAt;
    private IReadOnlyList<DetectedKeypoint> _lastKeypoints = [];
    private double[]? _lastSceneSignature;
    private IReadOnlyList<OnCourtObjectDetection> _lastOtherDetections = [];
    private NormalizedRect? _lastScoreboardObjectRegion;
    private readonly RelayCommand _reclassifyCommand;
    private readonly int _detectionIntervalFrames;
    private int _frameCounter;

    public MainWindowViewModel(
        IFrameSource frameSource,
        ICaptureSourceEnumerator sourceEnumerator,
        SportClassificationCoordinator sportCoordinator,
        CourtCalibrationCoordinator calibrationCoordinator,
        ICourtKeypointDetector keypointDetector,
        IMultiClassObjectDetector multiClassObjectDetector,
        IPlayerTracker playerTracker,
        IScoreboardOcrEngine scoreboardOcr,
        ISourceProfileStore profileStore,
        IJerseyNumberRecognizer jerseyNumberRecognizer,
        IJerseyNumberVoteAggregator jerseyNumberVoteAggregator,
        PlaybackRegionCoordinator playbackRegionCoordinator,
        // Optional (unlike every other collaborator above) so the many existing call sites that don't care
        // about ball detection specifically don't all need updating just to pass a Null-object placeholder -
        // defaults to the same "no model/no signal" degraded posture NullMultiClassObjectDetector already
        // gives multiClassObjectDetector when its own model file is absent.
        IMultiClassObjectDetector? ballDetector = null,
        int detectionIntervalFrames = 3)
    {
        _frameSource = frameSource;
        _sportCoordinator = sportCoordinator;
        _calibrationCoordinator = calibrationCoordinator;
        _keypointDetector = keypointDetector;
        _multiClassObjectDetector = multiClassObjectDetector;
        _playerTracker = playerTracker;
        _ballDetector = ballDetector ?? new NullMultiClassObjectDetector();
        _scoreboardOcr = scoreboardOcr;
        _profileStore = profileStore;
        _jerseyNumberRecognizer = jerseyNumberRecognizer;
        _jerseyNumberVoteAggregator = jerseyNumberVoteAggregator;
        _playbackRegionCoordinator = playbackRegionCoordinator;
        _detectionIntervalFrames = detectionIntervalFrames;

        SourcePicker = new SourcePickerViewModel(sourceEnumerator);
        RawOverlay = new RawOverlayViewModel();
        Minimap = new MinimapViewModel();
        SportIndicator = new SportIndicatorViewModel();
        ManualCalibration = new ManualCalibrationViewModel(calibrationCoordinator);
        KeypointSettings = new KeypointDetectionSettingsViewModel
        {
            KeypointConfidenceThreshold = keypointDetector.KeypointConfidenceThreshold,
            DetectionConfidenceThreshold = keypointDetector.DetectionConfidenceThreshold,
        };
        _reclassifyCommand = new RelayCommand(Reclassify, () => _currentSourceKey is not null);

        _frameSource.FrameArrived += OnFrameArrived;
        SourcePicker.PropertyChanged += OnSourcePickerPropertyChanged;
        KeypointSettings.PropertyChanged += OnKeypointSettingsPropertyChanged;

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

    /// <summary>Live court-keypoint detection thresholds, bound to sliders in the toolbar - pushed into <see cref="_keypointDetector"/> on every change so adjusting "鬆緊" takes effect on the next detected frame without restarting anything.</summary>
    public KeypointDetectionSettingsViewModel KeypointSettings { get; }

    private void OnKeypointSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _keypointDetector.KeypointConfidenceThreshold = KeypointSettings.KeypointConfidenceThreshold;
        _keypointDetector.DetectionConfidenceThreshold = KeypointSettings.DetectionConfidenceThreshold;
    }

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

        // Null (not the previous source's classification) so the retry gate in OnFrameArrived recognizes this
        // as needing classification, and default so it's attempted on this source's very first frame rather
        // than waiting out whatever fraction of the retry interval happened to remain from the old source.
        SportIndicator.Current = null;
        _lastSportClassificationCheckAt = default;

        // Reuse a region detected for this exact source in an earlier session (mirrors calibration/scoreboard
        // reuse below); null just means "not detected yet" - accumulation resumes on this source's own frames.
        _currentPlaybackRegion = _playbackRegionCoordinator.GetPersistedRegion(_currentSourceKey);
        ManualCalibration.SourceKey = _currentSourceKey;

        // No detected scoreboard region carries over from an unrelated source - falls back to the
        // default/manual region (ocr/scoreboard-recognition spec) until this source's own detections populate it.
        _lastScoreboardObjectRegion = null;

        // An unrelated source's last frame has nothing to do with this one - without this reset, the first
        // scene-cut check against a brand new source would compare it to a stale signature from whatever was
        // playing before and could spuriously invalidate a calibration this source hasn't even computed yet.
        _lastSceneSignature = null;

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

    private static OnCourtObjectDetection OffsetToFullFrame(OnCourtObjectDetection detection, int left, int top) =>
        new(detection.Left + left, detection.Top + top, detection.Right + left, detection.Bottom + top, detection.Confidence, detection.ClassName);

    private static MultiClassDetectionResult OffsetToFullFrame(MultiClassDetectionResult result, int left, int top) =>
        new(
            result.Players.Select(d => OffsetToFullFrame(d, left, top)).ToList(),
            result.Others.Select(d => OffsetToFullFrame(d, left, top)).ToList());

    private static DetectedKeypoint OffsetToFullFrame(DetectedKeypoint keypoint, int left, int top) =>
        keypoint with { Position = new ImagePoint(keypoint.Position.X + left, keypoint.Position.Y + top) };

    // Computed over whatever reference-frame-relative pixel space the caller's detections are already in (the
    // playback crop's own pixel space when cropped, else the full frame) - see ocr/scoreboard-recognition
    // spec's crop-region requirements and design.md's "Union box is computed over image-pixel coordinates,
    // then normalized against the same reference frame" decision. Caller guarantees a non-empty list.
    private static NormalizedRect ComputeNormalizedUnion(IReadOnlyList<OnCourtObjectDetection> detections, int referenceWidth, int referenceHeight)
    {
        var left = detections.Min(d => d.Left);
        var top = detections.Min(d => d.Top);
        var right = detections.Max(d => d.Right);
        var bottom = detections.Max(d => d.Bottom);

        return new NormalizedRect(
            left / referenceWidth,
            top / referenceHeight,
            (right - left) / referenceWidth,
            (bottom - top) / referenceHeight);
    }

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

        // Sport classification gates everything below it - player detection, jersey OCR, scoreboard OCR, and
        // keypoint detection all only make sense once a sport is known, so none of them are worth starting
        // while the source is still Unknown (e.g. the opening frames are a crowd shot, replay, or commentator
        // cut-in rather than the court itself). Retried on this throttled wall-clock cadence rather than every
        // frame, since classification is a real inference call; an Unknown result is never cached as final
        // (SportClassificationCoordinator.ClassifyAndCache), so this naturally keeps retrying on later frames
        // instead of getting stuck on a bad first attempt.
        if (_currentSourceKey is { } sourceKeyForClassification)
        {
            var needsClassification = SportIndicator.Current is not { Status: not SportClassificationStatus.Unknown };
            if (needsClassification && frame.Timestamp - _lastSportClassificationCheckAt >= SportClassificationRetryInterval)
            {
                _lastSportClassificationCheckAt = frame.Timestamp;
                var classification = _sportCoordinator.ClassifyOrGetCached(
                    sourceKeyForClassification,
                    () => new SportClassificationCoordinator.FrameSnapshot(frame.Pixels.ToArray(), frame.Width, frame.Height, frame.Stride));
                ApplyClassification(sourceKeyForClassification, classification);
                needsClassification = classification.Status == SportClassificationStatus.Unknown;
            }

            if (needsClassification)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RawOverlay.CurrentFrame = bitmap;
                    RawOverlay.SetAnnotations([]);
                });
                return;
            }
        }

        // Only a Confident sport has a registered geometry/keypoint detector - RecognizedButUnsupported never
        // runs keypoint detection at all, regardless of the settings below.
        var sport = SportIndicator.Current?.Sport;
        var isConfidentSport = sport is not null && SportIndicator.Current!.Status == SportClassificationStatus.Confident;

        // Keypoint detection runs here, ahead of player detection, so RequireKeypointsBeforeObjectDetection
        // below can see this frame's freshest result rather than one that's a whole detection pass stale.
        // Throttled to ~6.7Hz (every 150ms) via a wall-clock timer, a separate cadence from player detection's
        // frame-count-based one below - the court's keypoints only move when the camera pans/zooms/cuts, so
        // re-running the ONNX inference on every single frame is wasted work. Between checks, the last
        // detected keypoints are reused so the gate below and the overlay/minimap don't flicker on skipped
        // frames.
        if (isConfidentSport && _currentSourceKey is { } sourceKeyForKeypoints
            && frame.Timestamp - _lastKeypointCheckAt >= KeypointDetectionInterval)
        {
            _lastKeypointCheckAt = frame.Timestamp;

            // A hard scene/camera cut (e.g. a highlight reel cutting to a different game/arena/camera angle
            // within the same continuous capture source) means any calibration already saved for this source
            // was fit against a court framing that no longer matches what's on screen - reusing it would keep
            // projecting players to nonsensical minimap positions indefinitely, since GetValidCalibration below
            // has no way to tell the homography is stale on its own. Checked on the same buffer and cadence as
            // keypoint detection, since that's the only consumer that cares.
            var sceneSignature = playbackCrop is { } sceneCrop
                ? SceneCutDetector.ComputeGridSignature(sceneCrop.Pixels, sceneCrop.Width, sceneCrop.Height, sceneCrop.Stride)
                : SceneCutDetector.ComputeGridSignature(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
            if (_lastSceneSignature is { } previousSceneSignature && SceneCutDetector.IsCut(previousSceneSignature, sceneSignature))
            {
                _calibrationCoordinator.Invalidate(sourceKeyForKeypoints);

                // A cut is a new scene with unrelated players in it - carrying old track identities across it
                // is exactly as wrong as carrying them across a source switch (see the same call and reasoning
                // in SelectSourceAsync). Left un-reset, the old clip's tracks keep existing (motion prediction
                // just fails to match anything in the new scene) alongside freshly spawned tracks for the new
                // clip's actual players, so the minimap doubles up: real players plus their old clip's ghosts.
                _playerTracker.Reset();
            }
            _lastSceneSignature = sceneSignature;

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

            // Broadcast camera work pans/zooms/tilts continuously *within* a single source - not just at hard
            // cuts (SceneCutDetector only catches those) - so a homography computed once from this source's
            // first framing goes stale as soon as the camera moves, well before anything looks like a "cut" to
            // that detector. Re-attempted on every keypoint-detection tick, regardless of whether a calibration
            // is already persisted, so the homography continuously tracks the live camera framing instead of
            // freezing on the first one. TryCalibrateFromKeypoints only overwrites the persisted calibration on
            // success (not enough/too-clustered keypoints this tick just fails without touching it), so a
            // frame where the court is briefly out of view still keeps projecting off the last good fit rather
            // than losing calibration entirely.
            var calibrationAttempt = _calibrationCoordinator.TryCalibrateFromKeypoints(sourceKeyForKeypoints, sport!.Value, _lastKeypoints);
            if (!calibrationAttempt.Success)
            {
                Console.WriteLine($"[auto-calibrate] {calibrationAttempt.FailureReason}");
            }
        }

        // Opt-in (off by default - see KeypointDetectionSettingsViewModel.RequireKeypointsBeforeObjectDetection):
        // when enabled, a Confident-sport frame with no currently-detected keypoints skips player/ball
        // detection, jersey OCR, and scoreboard OCR below entirely, since there's no court to place any of
        // that on. Never gates a RecognizedButUnsupported/Unknown source - those never have keypoints to
        // begin with, and blocking them here would just be a second, redundant version of the classification
        // gate above.
        if (isConfidentSport && KeypointSettings.RequireKeypointsBeforeObjectDetection && _lastKeypoints.Count == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RawOverlay.CurrentFrame = bitmap;
                RawOverlay.SetAnnotations([]);
            });
            return;
        }

        // Player detection is independent of *calibration* state (vision/player-detection spec) - it doesn't
        // need a court projection to run - but it is gated behind sport classification (and optionally
        // keypoint detection) by the blocks above. Among frames that do reach here, it only runs every
        // `_detectionIntervalFrames`th captured frame - it's the most expensive step in this pipeline,
        // and the tracker's own motion model (PredictOnly, below) can carry a track's position between real
        // detections. Guarded because an inference failure on one frame (e.g. an unsupported ONNX op on this
        // machine's runtime build) must not take down the capture loop that calls this handler - see
        // MacFrameSource.PollLoopAsync, which has no catch-all of its own around FrameArrived subscribers.
        var isDetectionFrame = _frameCounter % _detectionIntervalFrames == 0;
        _frameCounter++;

        IReadOnlyList<TrackedPlayer> trackedPlayers;
        if (isDetectionFrame)
        {
            // Two separate inference passes, over two separate trained models: _multiClassObjectDetector
            // (Player/Ref) and _ballDetector (Ball-only) - the player-detection export no longer includes a
            // "Ball" class, so there's no longer a single shared model both can come from. Both results come
            // back in the crop's own pixel space (or full-frame space when no crop is active), the same
            // reference space NormalizedRect.DefaultScoreboardRegion/SourceProfile.ScoreboardRegion are already
            // normalized against - so the scoreboard-region computation just below reads the merged result
            // before it's offset to full-frame coordinates for the tracker/overlay uses further down.
            MultiClassDetectionResult detectionResult;
            try
            {
                var playerResult = playbackCrop is { } playerCrop
                    ? _multiClassObjectDetector.Detect(playerCrop.Pixels, playerCrop.Width, playerCrop.Height, playerCrop.Stride)
                    : _multiClassObjectDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
                var ballResult = playbackCrop is { } ballCrop
                    ? _ballDetector.Detect(ballCrop.Pixels, ballCrop.Width, ballCrop.Height, ballCrop.Stride)
                    : _ballDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);

                detectionResult = new MultiClassDetectionResult(
                    playerResult.Players,
                    [.. playerResult.Others, .. ballResult.Others]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[player-detect] detection failed for this frame, treating as none: {ex.Message}");
                detectionResult = new MultiClassDetectionResult(Players: [], Others: []);
            }

            // Overwritten only when this frame actually has a scoreboard-related detection - otherwise the
            // previously cached region (if any) keeps being used (design.md's "no explicit staleness expiry").
            var scoreboardCandidates = detectionResult.Others.Where(d => ScoreboardRelatedClassNames.Contains(d.ClassName)).ToList();
            if (scoreboardCandidates.Count > 0)
            {
                var referenceWidth = playbackCrop?.Width ?? frame.Width;
                var referenceHeight = playbackCrop?.Height ?? frame.Height;
                _lastScoreboardObjectRegion = ComputeNormalizedUnion(scoreboardCandidates, referenceWidth, referenceHeight);
            }

            var fullFrameResult = playbackCrop is { } offsetCrop
                ? OffsetToFullFrame(detectionResult, offsetCrop.Left, offsetCrop.Top)
                : detectionResult;

            // Detections are fed through the tracker to attach a stable track ID per player (tracking/player-tracking
            // spec) before rendering, so the same physical player keeps the same box/ID across frames instead of an
            // unlabeled per-frame foot-point.
            trackedPlayers = _playerTracker.Update(fullFrameResult.Players);
            _lastOtherDetections = fullFrameResult.Others;
        }
        else
        {
            // No detection was attempted this frame - advance motion prediction only, so tracked boxes keep
            // moving smoothly instead of freezing, without counting this frame against any track's occlusion
            // buffer (tracking/player-tracking spec's "Advance motion prediction without detection"). The
            // cached non-Player detections and scoreboard region are reused as-is until the next detection
            // frame, the same posture _lastKeypoints already has below.
            trackedPlayers = _playerTracker.PredictOnly();
        }

        var otherAnnotations = _lastOtherDetections
            .Select(d => OverlayAnnotation.ForBox(d.Left, d.Top, d.Right, d.Bottom, d.ClassName, d.ClassName))
            .ToList();

        var playerAnnotations = trackedPlayers
            .Select(t => OverlayAnnotation.ForBox(t.Left, t.Top, t.Right, t.Bottom, $"#{t.TrackId}"))
            .ToList();

        // Recognition + voting run unconditionally alongside tracking, before the sport/calibration gates
        // below (design.md's "lets vote counts warm up from the moment a track exists"), so a resolved number
        // is already available the moment calibration completes. A plain loop (not LINQ) because
        // frame.Pixels.Span is a ref struct and can't be captured into a lambda's closure.
        var jerseyNumberResults = new List<(int TrackId, JerseyNumberRecognitionResult Result)>(trackedPlayers.Count);
        foreach (var t in trackedPlayers)
        {
            JerseyNumberRecognitionResult jerseyResult;
            try
            {
                jerseyResult = _jerseyNumberRecognizer.Recognize(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride, t.Left, t.Top, t.Right, t.Bottom);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[jersey-ocr] recognition failed for track {t.TrackId}, treating as unrecognized: {ex.Message}");
                jerseyResult = new JerseyNumberRecognitionResult(Number: null, Confidence: 0f);
            }

            jerseyNumberResults.Add((t.TrackId, jerseyResult));
        }

        var resolvedJerseyNumbers = _jerseyNumberVoteAggregator.Update(jerseyNumberResults);

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
                RawOverlay.SetAnnotations(playerAnnotations.Concat(otherAnnotations));
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
                // Manual override always wins outright (so a user's fix for a source where automatic placement
                // is wrong doesn't silently stop working), then the most recent detected scoreboard-object
                // union box, then the fixed default - ocr/scoreboard-recognition spec's crop-region precedence.
                var region = _profileStore.Load(sourceKey)?.ScoreboardRegion ?? _lastScoreboardObjectRegion ?? NormalizedRect.DefaultScoreboardRegion;

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

        // isConfidentSport was already established above (before keypoint detection ran) - RecognizedButUnsupported
        // has no registered geometry to project keypoints/players onto, so it stops here with just the raw
        // overlay + player/other annotations, same as before. The redundant `sport is null` check (isConfidentSport
        // already implies it) is what lets the compiler narrow `sport` to non-null for the rest of the method.
        if (sport is null || !isConfidentSport)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RawOverlay.CurrentFrame = bitmap;
                RawOverlay.SetAnnotations(playerAnnotations.Concat(otherAnnotations));
            });
            return;
        }

        // Keypoint detection itself already ran in the throttled block above, ahead of player detection -
        // this just renders whatever _lastKeypoints currently holds.
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

            // Only players are plotted on the minimap - court keypoints are calibration's internal input, not
            // something a viewer needs to see once the diagram itself already draws the court lines. Foot point
            // (bottom-center of the box) rather than the box itself - the minimap plots a single court-space
            // position per player, not an area (dual-view-shell spec's "Minimap plots tracked players' court
            // positions"). Carries the track's sampled jersey color so MinimapView.axaml.cs can fill each
            // marker with it instead of one flat color for every player.
            var playerMarkers = trackedPlayers
                .OrderBy(t => t.FramesSinceMatch)
                .ThenByDescending(t => t.Confidence)
                .Take(MaxPlayersOnCourtSimultaneously)
                .Select(t =>
                {
                    var footPoint = new ImagePoint((t.Left + t.Right) / 2, t.Bottom);
                    var court = PointProjector.Project(calibration, footPoint);
                    var label = resolvedJerseyNumbers.TryGetValue(t.TrackId, out var number) ? $"#{number}" : $"#{t.TrackId}";
                    return new CourtMarker(court.X, court.Y, label, "player", t.Color);
                });

            markers = playerMarkers.ToList();
        }

        Dispatcher.UIThread.Post(() =>
        {
            RawOverlay.CurrentFrame = bitmap;
            RawOverlay.SetAnnotations(playerAnnotations.Concat(keypointAnnotations).Concat(otherAnnotations));

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
        KeypointSettings.PropertyChanged -= OnKeypointSettingsPropertyChanged;
        await _frameSource.DisposeAsync();
    }
}

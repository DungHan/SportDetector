using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private readonly IBallTracker _ballTracker;
    private readonly IMultiClassObjectDetector _ballDetector;
    private readonly IScoreboardOcrEngine _scoreboardOcr;
    private readonly ISourceProfileStore _profileStore;
    private readonly IJerseyNumberRecognizer _jerseyNumberRecognizer;
    private readonly IJerseyNumberVoteAggregator _jerseyNumberVoteAggregator;
    private readonly PlaybackRegionCoordinator _playbackRegionCoordinator;
    private readonly GameStateTracker _gameStateTracker = new();

    private static readonly TimeSpan KeypointDetectionInterval = TimeSpan.FromMilliseconds(150);

    // Above this cheap masked background-change score (see SceneCutDetector.ComputeBackgroundChangeScore), the
    // camera itself is judged to have moved enough since the last real keypoint detection to be worth re-running
    // it - deliberately lower than SceneCutDetector.DefaultChangeThreshold (0.2, "this is a different scene
    // entirely") since this just needs to catch an ordinary pan/zoom mid-shot, not a hard cut. A placeholder,
    // not yet empirically tuned against real broadcast footage (same caveat as SceneCutDetector's own threshold).
    private const double KeypointRefreshChangeThreshold = 0.05;

    // Safety net for keypoint detection's background-change gate below: even if the cheap masked signature never
    // reports enough change to trigger a refresh (e.g. a very slow drift that never crosses the threshold in one
    // 150ms tick, or a frame so full of players that ComputeBackgroundChangeScore keeps returning null), force a
    // real re-detection at least this often so calibration can't go stale indefinitely.
    private static readonly TimeSpan KeypointDetectionMaxInterval = TimeSpan.FromSeconds(4);

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

    /// <summary>Minimap marker opacity for a track currently coasting on motion prediction (no real detection matched this frame) rather than terminated outright - see the player-marker block in <see cref="ProcessFrame"/>.</summary>
    private const double FadedTrackOpacity = 0.4;

    private string? _currentSourceKey;
    private NormalizedRect? _currentPlaybackRegion;
    private DateTimeOffset _lastScoreboardCheckAt;
    private DateTimeOffset _lastKeypointCheckAt;
    private DateTimeOffset _lastSportClassificationCheckAt;
    private IReadOnlyList<DetectedKeypoint> _lastKeypoints = [];
    private double[]? _lastSceneSignature;
    private double[]? _lastKeypointSceneSignature;
    private DateTimeOffset _lastKeypointDetectionAt;
    private IReadOnlyList<TrackedPlayer> _lastTrackedPlayers = [];
    private BallPosition? _lastBallPosition;
    private IReadOnlyList<OnCourtObjectDetection> _lastOtherDetections = [];
    private NormalizedRect? _lastScoreboardObjectRegion;
    private readonly RelayCommand _reclassifyCommand;
    private readonly int _detectionIntervalFrames;
    private int _frameCounter;
    private long _perfFrameSequence;
    private DateTimeOffset? _perfLastFrameArrivedAt;
    private static int _colorDebugDumpCount;

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
        // Optional for the same reason ballDetector is: most existing call sites predate ball tracking and
        // don't care about it specifically. Defaults to a real BallTracker (not a null-object) - unlike the
        // detectors above, this is a pure algorithm over already-in-memory boxes with no missing-model
        // degraded path (same posture as playerTracker/ByteTrackPlayerTracker), so there is nothing to
        // degrade to.
        IBallTracker? ballTracker = null,
        int detectionIntervalFrames = 3)
    {
        _frameSource = frameSource;
        _sportCoordinator = sportCoordinator;
        _calibrationCoordinator = calibrationCoordinator;
        _keypointDetector = keypointDetector;
        _multiClassObjectDetector = multiClassObjectDetector;
        _playerTracker = playerTracker;
        _ballDetector = ballDetector ?? new NullMultiClassObjectDetector();
        _ballTracker = ballTracker ?? new BallTracker();
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

        // Same reasoning as _playerTracker.Reset() above - the ball's filtered position has no relationship
        // to an unrelated source's own ball.
        _ballTracker.Reset();

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

    // TEMPORARY debug aid: dumps the full playback crop plus each player's sampled upper-body sub-rectangle to
    // /tmp/color-debug so the actual sampled pixels can be inspected visually - remove once the color-sampling
    // bug is diagnosed.
    private static void DumpColorDebugImages(CroppedFrame crop, IReadOnlyList<PlayerDetection> players)
    {
        var dir = "/tmp/color-debug";
        Directory.CreateDirectory(dir);

        FrameBitmapConverter.ToWriteableBitmap(crop.Pixels, crop.Width, crop.Height, crop.Stride)
            .Save(Path.Combine(dir, $"dump{_colorDebugDumpCount}_full.png"));

        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            var box = FrameCropper.Crop(crop.Pixels, crop.Width, crop.Height, crop.Stride,
                p.Left / crop.Width, p.Top / crop.Height, (p.Right - p.Left) / crop.Width, (p.Bottom - p.Top) / crop.Height);
            if (box.Width > 0 && box.Height > 0)
            {
                FrameBitmapConverter.ToWriteableBitmap(box.Pixels, box.Width, box.Height, box.Stride)
                    .Save(Path.Combine(dir, $"dump{_colorDebugDumpCount}_p{i}_box.png"));
            }

            var upperBody = UpperBodyColorSampling.UpperBodyRectangle(p.Left, p.Top, p.Right, p.Bottom);
            if (upperBody is { } rect)
            {
                var sample = FrameCropper.Crop(crop.Pixels, crop.Width, crop.Height, crop.Stride,
                    rect.Left / crop.Width, rect.Top / crop.Height,
                    (rect.Right - rect.Left) / crop.Width, (rect.Bottom - rect.Top) / crop.Height);
                if (sample.Width > 0 && sample.Height > 0)
                {
                    FrameBitmapConverter.ToWriteableBitmap(sample.Pixels, sample.Width, sample.Height, sample.Stride)
                        .Save(Path.Combine(dir, $"dump{_colorDebugDumpCount}_p{i}_sample.png"));
                }

                Console.WriteLine($"[color-debug] p{i} box=({p.Left:F0},{p.Top:F0},{p.Right:F0},{p.Bottom:F0}) sample=({rect.Left:F0},{rect.Top:F0},{rect.Right:F0},{rect.Bottom:F0})");
            }
            else
            {
                Console.WriteLine($"[color-debug] p{i} box=({p.Left:F0},{p.Top:F0},{p.Right:F0},{p.Bottom:F0}) sample=skipped (dive/degenerate aspect ratio)");
            }
        }
    }

    /// <summary>
    /// Snaps every currently-tracked player's own color to whichever of this frame's two team-color clusters
    /// it's nearest, using the same <see cref="TwoMeansColorClusterer"/> <see cref="ByteTrackPlayerTracker"/>
    /// already runs internally for its match veto - a real broadcast has exactly two jersey colors on court, so
    /// clustering across every player at once (rather than trusting each track's own independently-drifted
    /// per-player EMA estimate) keeps every player on the same team rendered with the same minimap fill.
    /// Falls back to each track's own unclustered color when fewer than two players carry one this frame -
    /// nothing to cluster.
    /// </summary>
    private static Dictionary<int, (byte R, byte G, byte B)?> AssignTeamDisplayColors(IReadOnlyList<TrackedPlayer> trackedPlayers)
    {
        var coloredCount = trackedPlayers.Count(t => t.Color.HasValue);
        if (coloredCount < 2)
        {
            return trackedPlayers.ToDictionary(t => t.TrackId, t => t.Color);
        }

        var centroids = TwoMeansColorClusterer.Cluster(
            trackedPlayers.Where(t => t.Color.HasValue).Select(t => t.Color!.Value).ToList());

        return trackedPlayers.ToDictionary(t => t.TrackId, t => NearestCentroidColor(t.Color, centroids));
    }

    private static (byte R, byte G, byte B)? NearestCentroidColor(
        (byte R, byte G, byte B)? color,
        ((double R, double G, double B) CentroidA, (double R, double G, double B) CentroidB) centroids)
    {
        if (color is not { } c)
        {
            return null;
        }

        var point = ((double)c.R, (double)c.G, (double)c.B);
        var nearest = TwoMeansColorClusterer.SquaredDistance(point, centroids.CentroidA)
            <= TwoMeansColorClusterer.SquaredDistance(point, centroids.CentroidB)
            ? centroids.CentroidA
            : centroids.CentroidB;

        return (
            (byte)Math.Clamp(Math.Round(nearest.R), 0, 255),
            (byte)Math.Clamp(Math.Round(nearest.G), 0, 255),
            (byte)Math.Clamp(Math.Round(nearest.B), 0, 255));
    }

    // Shared tail of both keypoint-detection paths (the synchronous cold-start one and the deferred/concurrent
    // one) - offsets the raw detector output back into full-frame coordinates, stores it as _lastKeypoints, and
    // attempts recalibration from it. TryCalibrateFromKeypoints only overwrites the persisted calibration on
    // success (not enough/too-clustered keypoints this attempt just fails without touching it), so a frame
    // where the court is briefly out of view still keeps projecting off the last good fit rather than losing
    // calibration entirely.
    private void ApplyKeypointDetectionResult(
        IReadOnlyList<DetectedKeypoint> detectedKeypoints, CroppedFrame? crop, string sourceKey, SportType sport, long sequence, long elapsedMs)
    {
        _lastKeypoints = crop is { } c
            ? detectedKeypoints.Select(k => OffsetToFullFrame(k, c.Left, c.Top)).ToList()
            : detectedKeypoints;
        Console.WriteLine($"[perf#{sequence}] keypoints={elapsedMs}ms");

        var calibrationAttempt = _calibrationCoordinator.TryCalibrateFromKeypoints(sourceKey, sport, _lastKeypoints);
        if (!calibrationAttempt.Success)
        {
            Console.WriteLine($"[auto-calibrate] {calibrationAttempt.FailureReason}");
        }
    }

    private static PlayerDetection OffsetToFullFrame(PlayerDetection detection, int left, int top) =>
        new(detection.Left + left, detection.Top + top, detection.Right + left, detection.Bottom + top, detection.Confidence, detection.Color);

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

    // Temporary diagnostic instrumentation for the "detection lags behind playback" investigation - reports
    // this handler's own wall-clock cost and the gap since it was last invoked (the two numbers that together
    // bound how stale the raw overlay/minimap can look), plus a per-stage breakdown logged inline in
    // ProcessFrame below (tagged with the same #{sequence} so a run's lines can be correlated). Remove once
    // the pipeline is fast enough that this isn't needed to know where time is going.
    private void OnFrameArrived(object? sender, FrameArrivedEventArgs e)
    {
        var sequence = ++_perfFrameSequence;
        var now = DateTimeOffset.UtcNow;
        var gapMs = _perfLastFrameArrivedAt is { } lastArrivedAt ? (now - lastArrivedAt).TotalMilliseconds : (double?)null;
        _perfLastFrameArrivedAt = now;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            ProcessFrame(e.Frame, sequence);
        }
        finally
        {
            var gapText = gapMs is { } gap ? $"{gap:F0}ms" : "n/a";
            Console.WriteLine($"[perf#{sequence}] handler total={stopwatch.ElapsedMilliseconds}ms gap-since-previous={gapText}");
        }
    }

    private void ProcessFrame(CapturedFrame frame, long sequence)
    {
        var bitmap = FrameBitmapConverter.ToWriteableBitmap(frame);

        // Auto-detects the sub-rectangle that's actually gameplay versus surrounding page chrome (YouTube
        // comments, recommended-video thumbnails, ...) that happens to sit inside the captured frame - once
        // found (and persisted per source), it stays cached in _currentPlaybackRegion and this stops running.
        if (_currentPlaybackRegion is null && _currentSourceKey is { } accumulatingSourceKey)
        {
            _currentPlaybackRegion = _playbackRegionCoordinator.Accumulate(accumulatingSourceKey, frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
        }

        // Drawn on every branch below (including the early-return ones) so the detected playback region is
        // visible on the raw overlay as soon as it's found, regardless of sport classification/calibration state.
        var playbackRegionAnnotations = _currentPlaybackRegion is { } currentRegion
            ? new[]
            {
                OverlayAnnotation.ForBox(
                    currentRegion.X * frame.Width,
                    currentRegion.Y * frame.Height,
                    (currentRegion.X + currentRegion.Width) * frame.Width,
                    (currentRegion.Y + currentRegion.Height) * frame.Height,
                    "playback region",
                    "playbackRegion"),
            }
            : [];

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
                var classifyStopwatch = Stopwatch.StartNew();
                var classification = _sportCoordinator.ClassifyOrGetCached(
                    sourceKeyForClassification,
                    () => new SportClassificationCoordinator.FrameSnapshot(frame.Pixels.ToArray(), frame.Width, frame.Height, frame.Stride));
                Console.WriteLine($"[perf#{sequence}] classify={classifyStopwatch.ElapsedMilliseconds}ms");
                ApplyClassification(sourceKeyForClassification, classification);
                needsClassification = classification.Status == SportClassificationStatus.Unknown;
            }

            if (needsClassification)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RawOverlay.CurrentFrame = bitmap;
                    RawOverlay.SetAnnotations(playbackRegionAnnotations);
                });
                return;
            }
        }

        // Only a Confident sport has a registered geometry/keypoint detector - RecognizedButUnsupported never
        // runs keypoint detection at all, regardless of the settings below.
        var sport = SportIndicator.Current?.Sport;
        var isConfidentSport = sport is not null && SportIndicator.Current!.Status == SportClassificationStatus.Confident;

        // The court's keypoints only move when the camera pans/zooms/cuts - most frames, the camera is static
        // and _lastKeypoints from an earlier tick is still perfectly valid, so this block's job is deciding
        // *whether* a re-detection is even worth running, not running it. The actual (expensive) ONNX pass, if
        // needed, is deferred to the combined concurrent-detection block below so it can overlap with player/
        // ball detection instead of stalling in front of them. This decision itself is still throttled to
        // ~6.7Hz (every 150ms) via a wall-clock timer, a separate cadence from player detection's frame-count-
        // based one below - it's cheap enough (a coarse grid-sampled color signature) to run at that cadence
        // regardless of whether it ends up triggering anything.
        var needsKeypointRefresh = false;
        string? keypointSourceKey = null;
        if (isConfidentSport && _currentSourceKey is { } sourceKeyForKeypoints
            && frame.Timestamp - _lastKeypointCheckAt >= KeypointDetectionInterval)
        {
            _lastKeypointCheckAt = frame.Timestamp;
            keypointSourceKey = sourceKeyForKeypoints;

            var sceneCropForSignature = playbackCrop;
            var signatureWidth = sceneCropForSignature?.Width ?? frame.Width;
            var signatureHeight = sceneCropForSignature?.Height ?? frame.Height;
            var sceneSignature = sceneCropForSignature is { } sceneCrop
                ? SceneCutDetector.ComputeGridSignature(sceneCrop.Pixels, sceneCrop.Width, sceneCrop.Height, sceneCrop.Stride)
                : SceneCutDetector.ComputeGridSignature(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);

            // Masks out cells covered by last frame's tracked player boxes (converted into this signature's own
            // pixel space - crop-local when a playback crop is active) so a player sprinting across the court
            // is never mistaken for the camera itself moving, below.
            var maskBoxes = sceneCropForSignature is { } maskCrop
                ? _lastTrackedPlayers.Select(t => (t.Left - maskCrop.Left, t.Top - maskCrop.Top, t.Right - maskCrop.Left, t.Bottom - maskCrop.Top)).ToList()
                : _lastTrackedPlayers.Select(t => (t.Left, t.Top, t.Right, t.Bottom)).ToList();
            var cellMask = SceneCutDetector.ComputeCellMask(maskBoxes, signatureWidth, signatureHeight);

            // A hard scene/camera cut (e.g. a highlight reel cutting to a different game/arena/camera angle
            // within the same continuous capture source) means any calibration already saved for this source
            // was fit against a court framing that no longer matches what's on screen - reusing it would keep
            // projecting players to nonsensical minimap positions indefinitely, since GetValidCalibration below
            // has no way to tell the homography is stale on its own. Checked on the same buffer and cadence as
            // this whole block.
            var frameToFrameChange = _lastSceneSignature is { } previousSceneSignature
                ? SceneCutDetector.ComputeBackgroundChangeScore(previousSceneSignature, sceneSignature, cellMask)
                : null;
            if (frameToFrameChange is { } frameChange && frameChange > SceneCutDetector.DefaultChangeThreshold)
            {
                _calibrationCoordinator.Invalidate(sourceKeyForKeypoints);

                // A cut is a new scene with unrelated players in it - carrying old track identities across it
                // is exactly as wrong as carrying them across a source switch (see the same call and reasoning
                // in SelectSourceAsync). Left un-reset, the old clip's tracks keep existing (motion prediction
                // just fails to match anything in the new scene) alongside freshly spawned tracks for the new
                // clip's actual players, so the minimap doubles up: real players plus their old clip's ghosts.
                _playerTracker.Reset();
                _ballTracker.Reset();
            }
            _lastSceneSignature = sceneSignature;

            // Broadcast camera work pans/zooms/tilts continuously *within* a single source, not just at hard
            // cuts, so a homography computed once from this source's first framing goes stale as soon as the
            // camera moves. Compared against the signature as of the *last actual keypoint detection* (not just
            // the previous tick), so slow drift that's individually below KeypointRefreshChangeThreshold each
            // tick still accumulates into a refresh once it adds up. A null score (too few background cells left
            // unmasked - e.g. a fast break spreading players across most of the court) is deliberately treated
            // as "no signal" rather than "changed" - KeypointDetectionMaxInterval below is the safety net for
            // that case instead.
            var backgroundChangeSinceLastDetection = _lastKeypointSceneSignature is { } lastDetectionSignature
                ? SceneCutDetector.ComputeBackgroundChangeScore(lastDetectionSignature, sceneSignature, cellMask)
                : null;
            var cameraLikelyMoved = backgroundChangeSinceLastDetection is { } score && score > KeypointRefreshChangeThreshold;

            // A cold start (or every attempt so far has failed) has no "last known good" keypoints to fall
            // back on, so RequireKeypointsBeforeObjectDetection just below - which reads _lastKeypoints
            // synchronously, this same tick - would otherwise always see an empty result and permanently block
            // player detection. Detected synchronously here (not deferred into the concurrent block below like
            // every other refresh) specifically so this tick's gate check can see it; every later refresh,
            // once there's a non-empty fallback to coast on while the detection runs, can safely overlap with
            // player/ball detection instead.
            var isColdStart = _lastKeypoints.Count == 0;
            needsKeypointRefresh = !isColdStart
                && (_lastKeypointSceneSignature is null
                    || cameraLikelyMoved
                    || frame.Timestamp - _lastKeypointDetectionAt >= KeypointDetectionMaxInterval);

            if (isColdStart)
            {
                var keypointStopwatch = Stopwatch.StartNew();
                try
                {
                    var detected = sceneCropForSignature is { } detectCrop
                        ? _keypointDetector.Detect(detectCrop.Pixels, detectCrop.Width, detectCrop.Height, detectCrop.Stride)
                        : _keypointDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
                    ApplyKeypointDetectionResult(detected, sceneCropForSignature, sourceKeyForKeypoints, sport!.Value, sequence, keypointStopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[keypoint-detect] detection failed for this frame, treating as none: {ex.Message}");
                    _lastKeypoints = [];
                }

                _lastKeypointDetectionAt = frame.Timestamp;
                _lastKeypointSceneSignature = sceneSignature;
            }
            else if (needsKeypointRefresh)
            {
                _lastKeypointDetectionAt = frame.Timestamp;
                _lastKeypointSceneSignature = sceneSignature;
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
                RawOverlay.SetAnnotations(playbackRegionAnnotations);
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
        if (isDetectionFrame || needsKeypointRefresh)
        {
            // Player/ball/keypoint detection are three independent inference passes over three separate
            // sessions with no shared state between them (each its own forward pass at its own input
            // resolution - see models/README.md) - only the subset actually due this frame is started (the two
            // gates above are on independent cadences: frame-count for player/ball, camera-motion for
            // keypoints, so most frames only need one of the two groups below, not both), and whichever subset
            // does run is run concurrently rather than sequentially, so this stage's wall-clock cost is roughly
            // max(playerMs, ballMs, keypointMs) instead of their sum. ProcessFrame itself is synchronous, so
            // this still blocks here until everything started finishes; only the Detect() calls themselves
            // overlap.
            Task<MultiClassDetectionResult>? playerTask = null;
            Task<MultiClassDetectionResult>? ballTask = null;
            Task<(IReadOnlyList<DetectedKeypoint> Keypoints, long ElapsedMs)>? keypointTask = null;

            if (isDetectionFrame)
            {
                playerTask = Task.Run(() => playbackCrop is { } playerCrop
                    ? _multiClassObjectDetector.Detect(playerCrop.Pixels, playerCrop.Width, playerCrop.Height, playerCrop.Stride)
                    : _multiClassObjectDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride));
                ballTask = Task.Run(() => playbackCrop is { } ballCrop
                    ? _ballDetector.Detect(ballCrop.Pixels, ballCrop.Width, ballCrop.Height, ballCrop.Stride)
                    : _ballDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride));
            }

            if (needsKeypointRefresh)
            {
                keypointTask = Task.Run(() =>
                {
                    var keypointStopwatch = Stopwatch.StartNew();
                    var result = playbackCrop is { } keypointCrop
                        ? _keypointDetector.Detect(keypointCrop.Pixels, keypointCrop.Width, keypointCrop.Height, keypointCrop.Stride)
                        : _keypointDetector.Detect(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);
                    return (result, keypointStopwatch.ElapsedMilliseconds);
                });
            }

            var detectStopwatch = Stopwatch.StartNew();
            MultiClassDetectionResult detectionResult = new(Players: [], Others: []);
            if (playerTask is not null && ballTask is not null)
            {
                try
                {
                    Task.WaitAll(playerTask, ballTask);
                    var playerResult = playerTask.Result;
                    var ballResult = ballTask.Result;

                    detectionResult = new MultiClassDetectionResult(
                        playerResult.Players,
                        [.. playerResult.Others, .. ballResult.Others]);

                    var coloredCount = 0;
                    foreach (var p in detectionResult.Players)
                    {
                        if (p.Color.HasValue)
                        {
                            coloredCount++;
                        }
                    }
                    Console.WriteLine($"[color-debug] players={detectionResult.Players.Count} colored={coloredCount} cropped={(playbackCrop is not null)}");

                    if (playbackCrop is { } dumpCrop && _colorDebugDumpCount < 2)
                    {
                        _colorDebugDumpCount++;
                        DumpColorDebugImages(dumpCrop, playerResult.Players);
                    }
                }
                catch (Exception ex)
                {
                    // Task.WaitAll wraps a faulted task's exception in an AggregateException - unwrap it so this
                    // log line still names the actual failure instead of "One or more errors occurred.".
                    var message = ex is AggregateException aggregate ? aggregate.InnerException?.Message ?? ex.Message : ex.Message;
                    Console.WriteLine($"[player-detect] detection failed for this frame, treating as none: {message}");
                    detectionResult = new MultiClassDetectionResult(Players: [], Others: []);
                }
            }

            if (keypointTask is not null)
            {
                try
                {
                    var (detectedKeypoints, keypointElapsedMs) = keypointTask.GetAwaiter().GetResult();
                    ApplyKeypointDetectionResult(detectedKeypoints, playbackCrop, keypointSourceKey!, sport!.Value, sequence, keypointElapsedMs);
                }
                catch (Exception ex)
                {
                    var message = ex is AggregateException aggregate ? aggregate.InnerException?.Message ?? ex.Message : ex.Message;
                    Console.WriteLine($"[keypoint-detect] detection failed for this frame, treating as none: {message}");
                    _lastKeypoints = [];
                }
            }

            if (isDetectionFrame)
            {
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

                // vision/on-court-object-detection's "Ball detections are rendered from ball-tracking's
                // smoothed output" requirement - the Ball-specific raw-view/minimap rendering below reads
                // _lastBallPosition instead of scanning _lastOtherDetections directly; every other non-Player
                // class still renders straight from _lastOtherDetections, unaffected.
                _lastBallPosition = _ballTracker.Update(fullFrameResult.Others);

                var trackedColoredCount = 0;
                foreach (var t in trackedPlayers)
                {
                    if (t.Color.HasValue)
                    {
                        trackedColoredCount++;
                    }
                }
                Console.WriteLine($"[color-debug] tracked={trackedPlayers.Count} trackedColored={trackedColoredCount}");

                Console.WriteLine($"[perf#{sequence}] detect={detectStopwatch.ElapsedMilliseconds}ms players={fullFrameResult.Players.Count}");
            }
            else
            {
                // No player/ball detection was due this frame (only the keypoint refresh above was) - advance
                // motion prediction only, so tracked boxes keep moving smoothly instead of freezing, without
                // counting this frame against any track's occlusion buffer (tracking/player-tracking spec's
                // "Advance motion prediction without detection").
                trackedPlayers = _playerTracker.PredictOnly();
                _lastBallPosition = _ballTracker.PredictOnly();
            }
        }
        else
        {
            // Neither player/ball detection nor a keypoint refresh was due this frame - advance motion
            // prediction only. The cached non-Player detections, scoreboard region, and keypoints are all
            // reused as-is until their own next respective refresh.
            trackedPlayers = _playerTracker.PredictOnly();
            _lastBallPosition = _ballTracker.PredictOnly();
        }

        _lastTrackedPlayers = trackedPlayers;

        // Ball is excluded here and rendered separately from _lastBallPosition (tracking/ball-tracking's
        // smoothed/coasted output) instead - every other non-Player class still renders straight from
        // _lastOtherDetections, unaffected (vision/on-court-object-detection spec).
        var otherAnnotations = _lastOtherDetections
            .Where(d => d.ClassName != "Ball")
            .Select(d => OverlayAnnotation.ForBox(d.Left, d.Top, d.Right, d.Bottom, d.ClassName, d.ClassName))
            .ToList();

        IReadOnlyList<OverlayAnnotation> ballAnnotations = _lastBallPosition is { } lastBallPosition
            ? [OverlayAnnotation.ForBox(lastBallPosition.Left, lastBallPosition.Top, lastBallPosition.Right, lastBallPosition.Bottom, "Ball", "Ball")]
            : [];

        var playerAnnotations = trackedPlayers
            .Select(t => OverlayAnnotation.ForBox(t.Left, t.Top, t.Right, t.Bottom, $"#{t.TrackId}"))
            .ToList();

        // Recognition + voting run unconditionally alongside tracking, before the sport/calibration gates
        // below (design.md's "lets vote counts warm up from the moment a track exists"), so a resolved number
        // is already available the moment calibration completes. A plain loop (not LINQ) because
        // frame.Pixels.Span is a ref struct and can't be captured into a lambda's closure.
        var jerseyOcrStopwatch = Stopwatch.StartNew();
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

        if (trackedPlayers.Count > 0)
        {
            Console.WriteLine($"[perf#{sequence}] jerseyOcr={jerseyOcrStopwatch.ElapsedMilliseconds}ms players={trackedPlayers.Count} avgPerPlayer={jerseyOcrStopwatch.ElapsedMilliseconds / (double)trackedPlayers.Count:F1}ms");
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
                RawOverlay.SetAnnotations(playerAnnotations.Concat(otherAnnotations).Concat(ballAnnotations).Concat(playbackRegionAnnotations));
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
            var scoreboardStopwatch = Stopwatch.StartNew();
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
                // Temporarily logging the full exception (not just ex.Message) - this has been failing on
                // every attempt in testing and ex.Message alone ("Object reference not set to an instance of
                // an object.") doesn't say where. Revert to ex.Message once the actual null site is found.
                Console.WriteLine($"[scoreboard-ocr] recognition failed for this frame, keeping last known state: {ex}");
            }

            Console.WriteLine($"[perf#{sequence}] scoreboardOcr={scoreboardStopwatch.ElapsedMilliseconds}ms");
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
                RawOverlay.SetAnnotations(playerAnnotations.Concat(otherAnnotations).Concat(ballAnnotations).Concat(playbackRegionAnnotations));
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
            // positions"). Carries the track's own color snapped to one of this frame's two team-color clusters
            // (see AssignTeamDisplayColors) so MinimapView.axaml.cs fills every player on a team with the same
            // color instead of each track's own independently-drifted shade.
            //
            // Sourced from IPlayerTracker.AllConfirmedTracks rather than trackedPlayers (the raw overlay's
            // stricter, occlusion-buffer-limited set) - a player whose detection briefly fails shouldn't just
            // vanish from the minimap the way a stale box would from the raw overlay; instead it keeps its last
            // known court position, faded by FadedTrackOpacity, until the tracker actually terminates the track.
            var allTracks = _playerTracker.AllConfirmedTracks;
            var teamDisplayColors = AssignTeamDisplayColors(allTracks);
            var playerMarkers = allTracks
                .OrderBy(t => t.FramesSinceMatch)
                .ThenByDescending(t => t.Confidence)
                .Take(MaxPlayersOnCourtSimultaneously)
                .Select(t =>
                {
                    var footPoint = new ImagePoint((t.Left + t.Right) / 2, t.Bottom);
                    var court = PointProjector.Project(calibration, footPoint);
                    var label = resolvedJerseyNumbers.TryGetValue(t.TrackId, out var number) ? $"#{number}" : $"#{t.TrackId}";
                    var opacity = t.FramesSinceMatch > 0 ? FadedTrackOpacity : 1.0;
                    return new CourtMarker(court.X, court.Y, label, "player", teamDisplayColors[t.TrackId], opacity);
                });

            // The homography only maps points that lie on the court's ground plane, so the ball is projected
            // from its box's bottom-center - the point closest to floor contact - rather than its box center,
            // mirroring the player foot-point logic above. This is still only exact while the ball is on or
            // near the floor (e.g. a dribble); an airborne ball has no floor-contact pixel to feed the
            // homography, so its minimap position will drift up in the air, but staying at "the ball's
            // lowest visible point" keeps that drift as small as a single-view homography allows.
            // Sourced from _lastBallPosition (tracking/ball-tracking's smoothed/coasted output) rather than
            // scanning _lastOtherDetections directly - the tracker already picks the single highest-confidence
            // Ball detection per attempt internally.
            CourtMarker? ballMarker = null;
            if (_lastBallPosition is { } ballPosition)
            {
                var floorPoint = new ImagePoint((ballPosition.Left + ballPosition.Right) / 2, ballPosition.Bottom);
                var court = PointProjector.Project(calibration, floorPoint);
                ballMarker = new CourtMarker(court.X, court.Y, null, "ball");
            }

            markers = ballMarker is { } resolvedBallMarker ? [.. playerMarkers, resolvedBallMarker] : playerMarkers.ToList();
        }

        Dispatcher.UIThread.Post(() =>
        {
            RawOverlay.CurrentFrame = bitmap;
            RawOverlay.SetAnnotations(playerAnnotations.Concat(keypointAnnotations).Concat(otherAnnotations).Concat(ballAnnotations).Concat(playbackRegionAnnotations));

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

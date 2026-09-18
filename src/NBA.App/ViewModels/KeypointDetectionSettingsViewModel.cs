using CommunityToolkit.Mvvm.ComponentModel;

namespace NBA.App.ViewModels;

/// <summary>Live-adjustable court-keypoint detection thresholds, bound to a slider in the toolbar so the operator can loosen/tighten detection without restarting the app.</summary>
public partial class KeypointDetectionSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial float KeypointConfidenceThreshold { get; set; }

    [ObservableProperty]
    public partial float DetectionConfidenceThreshold { get; set; }

    /// <summary>
    /// When true, a Confident-sport frame with no currently-detected court keypoints skips player/ball
    /// detection, jersey OCR, and scoreboard OCR entirely for that frame - there's no court to place anything
    /// on, so running the rest of the pipeline is wasted work. Off by default: keypoint detection is less
    /// reliable than sport classification (see the thresholds above), so a run of false-negative frames would
    /// otherwise blank out player detection too - this is opt-in until keypoint detection proves stable enough.
    /// </summary>
    [ObservableProperty]
    public partial bool RequireKeypointsBeforeObjectDetection { get; set; }
}

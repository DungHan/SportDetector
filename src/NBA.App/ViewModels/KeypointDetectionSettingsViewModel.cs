using CommunityToolkit.Mvvm.ComponentModel;

namespace NBA.App.ViewModels;

/// <summary>Live-adjustable court-keypoint detection thresholds, bound to a slider in the toolbar so the operator can loosen/tighten detection without restarting the app.</summary>
public partial class KeypointDetectionSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial float KeypointConfidenceThreshold { get; set; }

    [ObservableProperty]
    public partial float DetectionConfidenceThreshold { get; set; }
}

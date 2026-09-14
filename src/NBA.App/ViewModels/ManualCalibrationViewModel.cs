using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NBA.Vision;

namespace NBA.App.ViewModels;

/// <summary>
/// Drives the in-app manual calibration workflow: user clicks reference points on the raw view and assigns
/// each a court landmark from the current sport's set, then submits for homography computation. See the
/// dual-view-shell spec's "In-app manual calibration workflow".
/// </summary>
public partial class ManualCalibrationViewModel : ViewModelBase
{
    private readonly CourtCalibrationCoordinator _coordinator;

    public ManualCalibrationViewModel(CourtCalibrationCoordinator coordinator)
    {
        _coordinator = coordinator;
        MarkedPoints.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RemainingLandmarkNames));
    }

    /// <summary>Set by the owning view model whenever the active capture source changes.</summary>
    public string SourceKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial SportType? CurrentSport { get; set; }

    [ObservableProperty]
    public partial string? ResultMessage { get; set; }

    [ObservableProperty]
    public partial string? SelectedLandmarkName { get; set; }

    public ObservableCollection<LandmarkCorrespondence> MarkedPoints { get; } = [];

    public IReadOnlyList<string> AvailableLandmarkNames =>
        CurrentSport is { } sport && CourtGeometryRegistry.TryGet(sport, out var geometry)
            ? geometry.Landmarks.Select(l => l.Name).ToList()
            : [];

    /// <summary>Landmarks not yet assigned to a marked point - what the picker should offer next.</summary>
    public IReadOnlyList<string> RemainingLandmarkNames =>
        AvailableLandmarkNames.Except(MarkedPoints.Select(p => p.LandmarkName)).ToList();

    partial void OnCurrentSportChanged(SportType? value)
    {
        OnPropertyChanged(nameof(AvailableLandmarkNames));
        OnPropertyChanged(nameof(RemainingLandmarkNames));
    }

    /// <summary>Adds the point currently pending click at <see cref="SelectedLandmarkName"/>, if one is selected. Returns false if there is no selected landmark to assign.</summary>
    public bool TryAddPointAtSelectedLandmark(ImagePoint point)
    {
        if (SelectedLandmarkName is not { } landmark)
        {
            return false;
        }

        AddPoint(point, landmark);
        SelectedLandmarkName = RemainingLandmarkNames.FirstOrDefault();
        return true;
    }

    [RelayCommand]
    private void Start()
    {
        MarkedPoints.Clear();
        ResultMessage = null;
        SelectedLandmarkName = AvailableLandmarkNames.FirstOrDefault();
        IsActive = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsActive = false;
        MarkedPoints.Clear();
        ResultMessage = null;
    }

    public void AddPoint(ImagePoint point, string landmarkName)
    {
        MarkedPoints.Add(new LandmarkCorrespondence(point, landmarkName));
    }

    public void RemoveLastPoint()
    {
        if (MarkedPoints.Count > 0)
        {
            MarkedPoints.RemoveAt(MarkedPoints.Count - 1);
        }
    }

    [RelayCommand]
    private void Submit()
    {
        var result = _coordinator.ManualCalibrate(SourceKey, CurrentSport, MarkedPoints.ToList());
        ResultMessage = result.Success ? "Calibration succeeded." : result.FailureReason;
        if (result.Success)
        {
            IsActive = false;
            MarkedPoints.Clear();
        }
    }
}

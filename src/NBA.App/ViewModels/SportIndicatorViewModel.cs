using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NBA.Vision;

namespace NBA.App.ViewModels;

/// <summary>Displays the current source's classified/overridden sport and offers a manual override. See the dual-view-shell spec's "Sport indicator and manual override".</summary>
public partial class SportIndicatorViewModel : ViewModelBase
{
    public SportIndicatorViewModel()
    {
        foreach (var sport in CourtGeometryRegistry.All.Keys)
        {
            SelectableSports.Add(sport);
        }
    }

    [ObservableProperty]
    public partial SportClassification? Current { get; set; }

    /// <summary>Every sport with a registered geometry - what the user can pick as a manual override.</summary>
    public ObservableCollection<SportType> SelectableSports { get; } = [];

    public string DisplayText => Current switch
    {
        null => "Sport: not yet determined",
        { Status: SportClassificationStatus.Unknown } => "Sport: unknown",
        { Status: SportClassificationStatus.RecognizedButUnsupported } c => $"Sport: {c.Sport} (not supported)",
        { IsManualOverride: true } c => $"Sport: {c.Sport} (manually set)",
        var c => $"Sport: {c!.Sport} ({c.Confidence:P0} confidence)",
    };

    public bool CanCalibrate => Current is { Status: SportClassificationStatus.Confident };

    partial void OnCurrentChanged(SportClassification? value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(CanCalibrate));
    }
}

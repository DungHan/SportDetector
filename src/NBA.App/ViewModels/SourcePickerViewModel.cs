using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NBA.Capture;

namespace NBA.App.ViewModels;

/// <summary>Lets the user browse and select/switch a capture source. See the dual-view-shell spec's "Source selection and switching in the UI".</summary>
public partial class SourcePickerViewModel : ViewModelBase
{
    private readonly ICaptureSourceEnumerator _enumerator;

    public SourcePickerViewModel(ICaptureSourceEnumerator enumerator)
    {
        _enumerator = enumerator;
        Refresh();
    }

    public ObservableCollection<CaptureSourceDescriptor> Sources { get; } = [];

    [ObservableProperty]
    public partial CaptureSourceDescriptor? SelectedSource { get; set; }

    [RelayCommand]
    private void Refresh()
    {
        var current = SelectedSource;
        Sources.Clear();
        foreach (var source in _enumerator.EnumerateSources())
        {
            Sources.Add(source);
        }

        // Preserve the selection across a refresh when the same source is still available.
        SelectedSource = current is not null && Sources.Contains(current)
            ? current
            : Sources.FirstOrDefault();
    }
}

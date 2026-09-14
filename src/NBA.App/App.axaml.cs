using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NBA.App.Services;
using NBA.App.ViewModels;
using NBA.App.Views;
using NBA.Vision;

namespace NBA.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var profileStore = new FileSourceProfileStore(GetProfileDirectory());

            var sportClassifierModelPath = Path.Combine(GetModelsDirectory(), "sport-classifier.onnx");
            var keypointModelPath = Path.Combine(GetModelsDirectory(), "court-keypoints.basketball.onnx");

            // No trained models are shipped in this change (see design.md's risk entries) - fall back to the
            // degraded/manual-only paths rather than failing to start.
            ISportClassifier sportClassifier = File.Exists(sportClassifierModelPath)
                ? new OnnxSportClassifier(sportClassifierModelPath, ["basketball"])
                : new NullSportClassifier();

            ICourtKeypointDetector keypointDetector = File.Exists(keypointModelPath)
                ? new OnnxCourtKeypointDetector(keypointModelPath, BasketballGeometryDefinition())
                : new NullCourtKeypointDetector(SportType.Basketball);

            var mainViewModel = new MainWindowViewModel(
                CapturePlatform.CreateFrameSource(),
                CapturePlatform.CreateSourceEnumerator(),
                new SportClassificationCoordinator(sportClassifier, profileStore),
                new CourtCalibrationCoordinator(profileStore),
                keypointDetector);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static CourtGeometryDefinition BasketballGeometryDefinition() =>
        CourtGeometryRegistry.TryGet(SportType.Basketball, out var geometry)
            ? geometry
            : throw new InvalidOperationException("Basketball geometry is expected to always be registered.");

    private static string GetModelsDirectory() => Path.Combine(AppContext.BaseDirectory, "models");

    private static string GetProfileDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NBA", "source-profiles");
}

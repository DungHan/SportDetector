using Avalonia;
using Avalonia.Headless;
using NBA.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace NBA.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::NBA.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

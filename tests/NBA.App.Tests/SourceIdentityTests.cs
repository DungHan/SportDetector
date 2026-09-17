using NBA.App.Services;
using NBA.Capture;
using Xunit;

namespace NBA.App.Tests;

public class SourceIdentityTests
{
    [Fact]
    public void DeriveKey_StripsTrailingAudioPlayingIndicator_SoItMatchesTheSilentTitlesKey()
    {
        var silent = new CaptureSourceDescriptor("a", "YouTube - some video", CaptureSourceKind.Window, 1, "chrome");
        var playing = new CaptureSourceDescriptor("a", "YouTube - some video \U0001F50A", CaptureSourceKind.Window, 1, "chrome");

        Assert.Equal(SourceIdentity.DeriveKey(silent), SourceIdentity.DeriveKey(playing));
    }

    [Fact]
    public void DeriveKey_StillDistinguishesGenuinelyDifferentTitles()
    {
        var videoOne = new CaptureSourceDescriptor("a", "YouTube - video one", CaptureSourceKind.Window, 1, "chrome");
        var videoTwo = new CaptureSourceDescriptor("a", "YouTube - video two", CaptureSourceKind.Window, 1, "chrome");

        Assert.NotEqual(SourceIdentity.DeriveKey(videoOne), SourceIdentity.DeriveKey(videoTwo));
    }
}

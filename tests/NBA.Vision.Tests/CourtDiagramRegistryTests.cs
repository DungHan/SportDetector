using NBA.Vision;

namespace NBA.Vision.Tests;

public class CourtDiagramRegistryTests
{
    [Theory]
    [InlineData("basketball")]
    [InlineData("soccer")]
    [InlineData("tennis")]
    [InlineData("american_football")]
    public void TryGet_EveryClassifiableSport_IsRegistered(string sportId)
    {
        Assert.True(CourtDiagramRegistry.TryGet(new SportType(sportId), out var spec));
        Assert.NotEmpty(spec.Markings);
        Assert.True(spec.SurfaceWidthMeters > 0);
        Assert.True(spec.SurfaceLengthMeters > 0);
    }

    [Fact]
    public void TryGet_UnknownSport_ReturnsFalse()
    {
        Assert.False(CourtDiagramRegistry.TryGet(SportType.Unknown, out _));
    }

    [Fact]
    public void Basketball_MatchesRegulationNbaCourtSize()
    {
        Assert.True(CourtDiagramRegistry.TryGet(SportType.Basketball, out var spec));
        Assert.InRange(spec.SurfaceLengthMeters, 28.0, 29.0); // 94 ft
        Assert.InRange(spec.SurfaceWidthMeters, 15.0, 15.5); // 50 ft
    }

    [Fact]
    public void Soccer_MatchesFifaRecommendedPitchSize()
    {
        Assert.True(CourtDiagramRegistry.TryGet(SportType.Soccer, out var spec));
        Assert.InRange(spec.SurfaceLengthMeters, 100.0, 110.0);
        Assert.InRange(spec.SurfaceWidthMeters, 64.0, 75.0);
    }

    [Fact]
    public void Tennis_MatchesDoublesCourtSize()
    {
        // Drawn portrait (baselines top/bottom) - the long baseline-to-baseline dimension is the "width" axis here.
        Assert.True(CourtDiagramRegistry.TryGet(SportType.Tennis, out var spec));
        Assert.InRange(spec.SurfaceWidthMeters, 23.5, 24.0);
        Assert.InRange(spec.SurfaceLengthMeters, 10.9, 11.0);
    }

    [Fact]
    public void AmericanFootball_MatchesNflFieldSizeIncludingEndZones()
    {
        Assert.True(CourtDiagramRegistry.TryGet(SportType.AmericanFootball, out var spec));
        Assert.InRange(spec.SurfaceLengthMeters, 109.0, 110.0); // 120 yd incl. end zones
        Assert.InRange(spec.SurfaceWidthMeters, 48.0, 49.0); // 53.33 yd
    }
}

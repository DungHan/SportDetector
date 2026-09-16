namespace NBA.Tracking.Tests;

public class TwoMeansColorClustererTests
{
    [Fact]
    public void Cluster_ClearlySeparableTwoColorSet_CentroidsLandNearEachRealColor()
    {
        var colors = new (byte R, byte G, byte B)[]
        {
            (250, 10, 10), (245, 15, 5), (255, 5, 12), // clustered near red
            (10, 10, 250), (5, 15, 245), (12, 5, 255), // clustered near blue
        };

        var (centroidA, centroidB) = TwoMeansColorClusterer.Cluster(colors);

        var red = (250.0, 10.0, 10.0);
        var blue = (10.0, 10.0, 250.0);

        // Order isn't guaranteed - one centroid should land near red, the other near blue.
        var (nearRed, nearBlue) = TwoMeansColorClusterer.SquaredDistance(centroidA, red) < TwoMeansColorClusterer.SquaredDistance(centroidA, blue)
            ? (centroidA, centroidB)
            : (centroidB, centroidA);

        Assert.True(TwoMeansColorClusterer.SquaredDistance(nearRed, red) < 200);
        Assert.True(TwoMeansColorClusterer.SquaredDistance(nearBlue, blue) < 200);
    }

    [Fact]
    public void Cluster_NearIdenticalSingleColorSet_CentroidsEndUpCloseTogether()
    {
        var colors = new (byte R, byte G, byte B)[]
        {
            (100, 100, 100), (101, 99, 100), (99, 101, 101), (100, 100, 99),
        };

        var (centroidA, centroidB) = TwoMeansColorClusterer.Cluster(colors);

        // No real two-way split exists in this data, so both centroids should sit within the tight noise band
        // the input colors occupy - a small squared distance apart, not spread across the full color range.
        Assert.True(TwoMeansColorClusterer.SquaredDistance(centroidA, centroidB) < 50);
    }

    [Fact]
    public void Cluster_FewerThanTwoColors_Throws()
    {
        var colors = new (byte R, byte G, byte B)[] { (100, 100, 100) };

        Assert.Throws<ArgumentException>(() => TwoMeansColorClusterer.Cluster(colors));
    }
}

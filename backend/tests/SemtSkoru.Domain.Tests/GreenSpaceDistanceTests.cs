using NetTopologySuite.Geometries;

namespace SemtSkoru.Domain.Tests;

public class GreenSpaceDistanceTests
{
    [Fact]
    public void HaversineMeters_returns_zero_for_identical_points()
    {
        var point = new Coordinate(29.03, 41.0);

        var distance = GeoDistance.HaversineMeters(point, point);

        Assert.Equal(0, distance, precision: 6);
    }

    [Fact]
    public void HaversineMeters_matches_the_known_one_degree_of_latitude_distance()
    {
        // One degree of latitude is ~111.32 km everywhere on Earth — a standard
        // geodesic constant, independent of this implementation, used here to
        // verify the formula rather than any Istanbul-specific value.
        var a = new Coordinate(0, 0);
        var b = new Coordinate(0, 1);

        var distance = GeoDistance.HaversineMeters(a, b);

        Assert.InRange(distance, 110_500, 111_500);
    }

    [Fact]
    public void HaversineMeters_is_symmetric()
    {
        var a = new Coordinate(29.03, 41.0);
        var b = new Coordinate(29.05, 40.99);

        Assert.Equal(GeoDistance.HaversineMeters(a, b), GeoDistance.HaversineMeters(b, a), precision: 6);
    }
}

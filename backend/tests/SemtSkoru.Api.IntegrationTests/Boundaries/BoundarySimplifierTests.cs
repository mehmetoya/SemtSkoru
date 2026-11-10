using NetTopologySuite.Algorithm.Distance;
using NetTopologySuite.Geometries;
using SemtSkoru.Api.Boundaries;

namespace SemtSkoru.Api.IntegrationTests.Boundaries;

// Pure geometry tests - no database needed. SeededBoundarySimplificationTests covers the same
// guarantees against the real seeded OSM boundaries (and the endpoint/cache wiring).
public class BoundarySimplifierTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    // A rough 1 km x 1 km square near Kadıköy whose bottom edge is a dense, slightly wobbly OSM-
    // style line: a vertex every ~1 m, each within ~0.2 m of the straight edge - exactly the kind of
    // sub-pixel detail the simplifier exists to drop.
    private static Polygon DenseSquare(double originX = 29.0, double originY = 40.98)
    {
        var coordinates = new List<Coordinate>();
        for (var i = 0; i <= 1000; i++)
        {
            var wobble = (i % 2 == 0 ? 1 : -1) * 0.0000017;
            coordinates.Add(new Coordinate(originX + i * 0.0000123456, originY + wobble));
        }

        var right = originX + 1000 * 0.0000123456;
        coordinates.Add(new Coordinate(right, originY + 0.0091234567));
        coordinates.Add(new Coordinate(originX, originY + 0.0091234567));
        coordinates.Add(new Coordinate(originX, originY + 0.0000017));
        return Factory.CreatePolygon(coordinates.ToArray());
    }

    private static int MaxDecimalPlaces(Geometry geometry) =>
        geometry.Coordinates
            .SelectMany(c => new[] { c.X, c.Y })
            .Max(DecimalPlaces);

    private static int DecimalPlaces(double value)
    {
        for (var places = 0; places <= 15; places++)
        {
            if (Math.Round(value, places) == value)
            {
                return places;
            }
        }

        return 16;
    }

    [Fact]
    public void Simplify_drops_sub_tolerance_vertices_and_keeps_a_valid_polygon()
    {
        var original = DenseSquare();

        var simplified = BoundarySimplifier.Simplify(original);

        Assert.True(original.IsValid);
        Assert.True(simplified.IsValid);
        Assert.IsType<Polygon>(simplified);
        Assert.True(simplified.NumPoints < original.NumPoints / 10,
            $"expected a large vertex reduction, got {original.NumPoints} -> {simplified.NumPoints}");
        Assert.Equal(original.Area, simplified.Area, original.Area * 0.01);
    }

    [Fact]
    public void Simplify_rounds_every_coordinate_to_five_decimal_places()
    {
        var original = DenseSquare();
        Assert.True(MaxDecimalPlaces(original) > BoundarySimplifier.DecimalPlaces);

        var simplified = BoundarySimplifier.Simplify(original);

        Assert.True(MaxDecimalPlaces(simplified) <= BoundarySimplifier.DecimalPlaces);
    }

    [Fact]
    public void Simplify_never_moves_the_outline_further_than_the_tolerance_plus_rounding()
    {
        var original = DenseSquare();

        var simplified = BoundarySimplifier.Simplify(original);

        // Half a unit in the 5th decimal place per axis is the most rounding can add on top of
        // the Douglas-Peucker tolerance.
        var bound = BoundarySimplifier.Tolerance + Math.Sqrt(2) * 0.5 * Math.Pow(10, -BoundarySimplifier.DecimalPlaces);
        Assert.True(DiscreteHausdorffDistance.Distance(original, simplified) <= bound);
    }

    [Fact]
    public void Simplify_keeps_every_island_of_a_multipolygon_and_its_type()
    {
        // Adalar/Şile-style: one big polygon plus small islets, the smallest only ~20 m across
        // (well inside a few tolerances, so plain Douglas-Peucker could collapse it).
        var islet = Factory.CreatePolygon(
        [
            new Coordinate(29.1, 40.9), new Coordinate(29.10025, 40.9),
            new Coordinate(29.10025, 40.90018), new Coordinate(29.1, 40.90018), new Coordinate(29.1, 40.9),
        ]);
        var original = Factory.CreateMultiPolygon([DenseSquare(), DenseSquare(29.05, 40.95), islet]);

        var simplified = BoundarySimplifier.Simplify(original);

        Assert.True(simplified.IsValid);
        Assert.IsType<MultiPolygon>(simplified);
        Assert.Equal(3, simplified.NumGeometries);
        Assert.True(simplified.NumPoints < original.NumPoints);
    }

    [Fact]
    public void Simplify_does_not_mutate_its_input()
    {
        // The stored boundary entity's own Geometry instance is what gets passed in - ingestion
        // jobs' point-in-polygon tests depend on that staying full-resolution.
        var original = DenseSquare();
        var snapshot = original.Copy();

        BoundarySimplifier.Simplify(original);

        Assert.True(original.EqualsExact(snapshot));
    }

    [Fact]
    public void Simplify_falls_back_to_six_decimals_when_five_would_make_the_ring_self_intersect()
    {
        // Two inward spikes whose tips are ~0.3 m apart - valid as stored, but rounding both tips
        // to 5 decimals lands them on the same point (28.9005, 41.001), which makes the ring touch
        // itself. Küçükçekmece's real boundary has a pinch like this (see BoundarySimplifier).
        var original = Factory.CreatePolygon(
        [
            new Coordinate(28.9, 41.0),
            new Coordinate(28.901, 41.0),
            new Coordinate(28.901, 41.0009),
            new Coordinate(28.9005019, 41.001),
            new Coordinate(28.901, 41.0011),
            new Coordinate(28.901, 41.002),
            new Coordinate(28.9, 41.002),
            new Coordinate(28.9, 41.0011),
            new Coordinate(28.9004981, 41.001),
            new Coordinate(28.9, 41.0009),
            new Coordinate(28.9, 41.0),
        ]);
        Assert.True(original.IsValid);

        var simplified = BoundarySimplifier.Simplify(original);

        Assert.True(simplified.IsValid);
        Assert.IsType<Polygon>(simplified);
        Assert.Equal(BoundarySimplifier.FallbackDecimalPlaces, MaxDecimalPlaces(simplified));
    }
}

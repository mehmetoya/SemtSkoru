using NetTopologySuite.Geometries;

namespace SemtSkoru.Domain.Tests;

public class NeighborhoodTests
{
    [Fact]
    public void Can_construct_with_id_name_and_boundary()
    {
        var boundary = new Polygon(new LinearRing([
            new Coordinate(29.0, 41.0),
            new Coordinate(29.1, 41.0),
            new Coordinate(29.1, 41.1),
            new Coordinate(29.0, 41.1),
            new Coordinate(29.0, 41.0),
        ]));

        var neighborhood = new Neighborhood
        {
            Id = "kadikoy",
            Name = "Kadıköy",
            Boundary = boundary,
        };

        Assert.Equal("kadikoy", neighborhood.Id);
        Assert.Equal("Kadıköy", neighborhood.Name);
        Assert.Same(boundary, neighborhood.Boundary);
    }
}

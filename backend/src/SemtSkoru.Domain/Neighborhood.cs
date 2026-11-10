using NetTopologySuite.Geometries;

namespace SemtSkoru.Domain;

public sealed class Neighborhood
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Geometry Boundary { get; init; }
}

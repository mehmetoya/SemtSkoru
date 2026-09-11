namespace SemtSkoru.Domain;

public sealed class GreenSpaceReading
{
    public required string NeighborhoodId { get; init; }
    public string NearestParkName { get; set; } = "";
    public double NearestParkDistanceMeters { get; set; }
    public required DataSourceMetadata Source { get; set; }
}

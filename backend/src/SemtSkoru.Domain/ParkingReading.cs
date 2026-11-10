namespace SemtSkoru.Domain;

public sealed class ParkingReading
{
    public required string NeighborhoodId { get; init; }
    public double AverageAvailabilityRatio { get; set; }
    public int FacilityCount { get; set; }
    public required DataSourceMetadata Source { get; set; }
}

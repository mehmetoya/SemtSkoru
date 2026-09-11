namespace SemtSkoru.Domain;

public sealed class TrafficReading
{
    public required string NeighborhoodId { get; init; }
    public double AverageSpeedKmh { get; set; }
    public int SampleCount { get; set; }
    public required DataSourceMetadata Source { get; set; }
}

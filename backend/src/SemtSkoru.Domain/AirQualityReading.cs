namespace SemtSkoru.Domain;

public sealed class AirQualityReading
{
    public required string NeighborhoodId { get; init; }
    public double AqiIndex { get; set; }
    public DateTimeOffset ReadingTime { get; set; }
    public required DataSourceMetadata Source { get; set; }
}

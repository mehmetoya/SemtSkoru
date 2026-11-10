namespace SemtSkoru.Domain;

public sealed class HealthAccessReading
{
    public required string NeighborhoodId { get; init; }

    /// <summary>The district's population-weighted average of İBB's mahalle-level
    /// SAGLIK_INDEX (see HealthAccessIngestionJob) - not yet scaled to 0-100.</summary>
    public double WeightedHealthIndex { get; set; }

    /// <summary>How many of the district's wards (mahalles) had usable (non-zero) population
    /// data and were included in the weighted average - kept for transparency, the same role
    /// ParkingReading.FacilityCount plays for parking.</summary>
    public int WardCount { get; set; }

    public required DataSourceMetadata Source { get; set; }
}

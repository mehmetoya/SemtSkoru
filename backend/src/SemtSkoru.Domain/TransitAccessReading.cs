namespace SemtSkoru.Domain;

public sealed class TransitAccessReading
{
    public required string NeighborhoodId { get; init; }

    /// <summary>Real İETT bus stops whose point geometry falls inside this district's boundary
    /// (point-in-polygon test - see TransitAccessIngestionJob) - kept for transparency, the same
    /// role FacilityCount/WardCount play for parking/health access.</summary>
    public int StopCount { get; set; }

    /// <summary>StopCount divided by the district's real physical area in km² (equirectangular
    /// approximation of the WGS84 boundary - see TransitAccessIngestionJob) - the actual
    /// quantity DimensionScoring.ScoreTransitAccess scores, so a large district isn't unfairly
    /// rewarded just for containing more stops.</summary>
    public double StopDensityPerKm2 { get; set; }

    public required DataSourceMetadata Source { get; set; }
}

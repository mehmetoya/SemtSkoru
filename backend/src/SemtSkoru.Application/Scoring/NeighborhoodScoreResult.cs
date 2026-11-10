using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

public sealed record NeighborhoodScoreResult(
    string NeighborhoodId,
    DimensionScore AirQuality,
    DimensionScore GreenSpace,
    DimensionScore Transportation,
    DimensionScore Parking,
    DimensionScore HealthAccess,
    DimensionScore TransitAccess,
    Score? Overall)
{
    public bool IsComplete =>
        AirQuality.HasData && GreenSpace.HasData && Transportation.HasData && Parking.HasData &&
        HealthAccess.HasData && TransitAccess.HasData;

    /// <summary>True if at least one dimension has a real reading - false for a neighborhood row
    /// that exists but has had nothing ingested for it yet (see NeighborhoodReadings' remarks).
    /// Used by DistrictSummaryService/DistrictSummaryGenerationJob to skip a district entirely
    /// (no Gemini call, no pacing delay spent) rather than asking the AI to summarize six nulls.</summary>
    public bool HasAnyData =>
        AirQuality.HasData || GreenSpace.HasData || Transportation.HasData || Parking.HasData ||
        HealthAccess.HasData || TransitAccess.HasData;
}

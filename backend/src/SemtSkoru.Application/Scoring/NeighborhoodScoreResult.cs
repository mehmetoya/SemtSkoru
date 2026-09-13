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
}

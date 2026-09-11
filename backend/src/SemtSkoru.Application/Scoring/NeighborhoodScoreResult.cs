using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

public sealed record NeighborhoodScoreResult(
    string NeighborhoodId,
    DimensionScore AirQuality,
    DimensionScore GreenSpace,
    DimensionScore Transportation,
    Score? Overall)
{
    public bool IsComplete => AirQuality.HasData && GreenSpace.HasData && Transportation.HasData;
}

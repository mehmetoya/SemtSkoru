using System.Globalization;
using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Summaries;

/// <summary>
/// A compact, deterministic encoding of a district's six dimension score VALUES (not their
/// freshness/source metadata) used purely to detect whether the numbers that would go into a
/// regenerated AI summary have actually changed since the last one - see
/// DistrictSummaryGenerationJob. Two NeighborhoodScoreResults for the same district produce the
/// same signature whenever every dimension's rounded 0-100 value (or its absence) is identical,
/// even when an ingestion job re-synced the exact same underlying reading (bumping
/// PublishedAt/FetchedAt without changing the score) - that re-sync must NOT burn a Gemini call
/// regenerating text that would come out identical.
/// </summary>
public static class DistrictSummarySignature
{
    public static string Compute(NeighborhoodScoreResult score) => string.Join(
        '|',
        Format(score.AirQuality),
        Format(score.GreenSpace),
        Format(score.Transportation),
        Format(score.Parking),
        Format(score.HealthAccess),
        Format(score.TransitAccess));

    private static string Format(DimensionScore dimension) =>
        dimension.Value?.Value.ToString(CultureInfo.InvariantCulture) ?? "null";
}

using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Trends;

/// <summary>
/// Determines which of a district's six dimensions changed enough between a baseline snapshot
/// and its current score to be worth narrating at all. This is the ONE place both
/// SemtSkoru.Infrastructure.Trends.ScoreSnapshotJob (to decide, with zero Gemini calls and zero
/// pacing delay, whether there is anything to even attempt for a district) and
/// DistrictTrendService (to decide what it's allowed to ground a prompt in, and to reject a
/// model response that invents a change outside this list) agree on what "meaningful" means, so
/// the two can never disagree about it.
/// </summary>
public static class DistrictTrendDeltas
{
    // Below this many points (out of 100), a delta is treated as noise, not a trend worth
    // telling a visitor about - e.g. the Overall score's own equal-weight average can drift by a
    // point between two runs purely from rounding, even when every dimension that feeds it is
    // unchanged. A real 1-2 point move is still a REAL number, never fabricated by this
    // threshold - it's simply judged not worth a sentence, the same "no fabrication" discipline
    // applied to what NOT to say as to what to say.
    public const int MinimumMeaningfulDelta = 3;

    private static readonly (string Key, Func<NeighborhoodScoreResult, int?> Current, Func<ScoreSnapshotBaseline, int?> Previous)[] Dimensions =
    [
        ("airQuality", r => r.AirQuality.Value?.Value, b => b.AirQuality),
        ("greenSpace", r => r.GreenSpace.Value?.Value, b => b.GreenSpace),
        ("transportation", r => r.Transportation.Value?.Value, b => b.Transportation),
        ("parking", r => r.Parking.Value?.Value, b => b.Parking),
        ("healthAccess", r => r.HealthAccess.Value?.Value, b => b.HealthAccess),
        ("transitAccess", r => r.TransitAccess.Value?.Value, b => b.TransitAccess),
    ];

    public static IReadOnlyList<DimensionDelta> Compute(ScoreSnapshotBaseline baseline, NeighborhoodScoreResult current)
    {
        var result = new List<DimensionDelta>();

        foreach (var (key, currentSelector, previousSelector) in Dimensions)
        {
            var currentValue = currentSelector(current);
            var previousValue = previousSelector(baseline);

            // Only ever compares a real number to another real number - a dimension that lacks
            // data on either side (never ingested yet at baseline time, or has since lost its
            // only reading) is skipped entirely rather than treated as a change to/from zero.
            if (currentValue is not { } cur || previousValue is not { } prev)
            {
                continue;
            }

            if (Math.Abs(cur - prev) < MinimumMeaningfulDelta)
            {
                continue;
            }

            result.Add(new DimensionDelta(key, prev, cur));
        }

        return result;
    }
}

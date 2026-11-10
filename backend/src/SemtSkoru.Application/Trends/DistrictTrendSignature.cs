using System.Globalization;

namespace SemtSkoru.Application.Trends;

/// <summary>
/// A compact, deterministic encoding of exactly which dimensions DistrictTrendDeltas found
/// meaningfully changed for a district, and their real previous/current values - used purely to
/// detect whether a district's "current vs. last-compared-snapshot" comparison has actually
/// changed since the last trend text was generated (see ScoreSnapshotJob). Deliberately built
/// from the deltas themselves, not from the baseline snapshot's own RecordedAt timestamp: the
/// baseline naturally slides forward on almost every run (last week's "most recent snapshot old
/// enough" ages out and a newer one takes its place), so keying off RecordedAt would force a
/// regeneration every single week even when the underlying numbers being compared never actually
/// moved - exactly the wasted-Gemini-call problem DistrictSummarySignature already avoids for
/// re-synced-but-unchanged readings, applied here to a rotating baseline instead.
/// </summary>
public static class DistrictTrendSignature
{
    public static string Compute(IReadOnlyList<DimensionDelta> deltas) => string.Join(
        ',',
        deltas
            .OrderBy(d => d.Dimension, StringComparer.Ordinal)
            .Select(d => string.Create(
                CultureInfo.InvariantCulture,
                $"{d.Dimension}:{d.PreviousScore}:{d.CurrentScore}")));
}

using SemtSkoru.Application.Comparisons;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Tests.Comparisons;

// ComparisonSummaryOrchestrator relies entirely on this signature to decide whether a cached pair
// is still valid - these tests prove it's stable when nothing meaningful changed on either side,
// different when EITHER district's real value changes, and - the one property unique to a PAIR
// signature - sensitive to argument order, which is exactly why callers MUST canonicalize which
// score is "A" and which is "B" before computing or comparing it (see
// ComparisonSummaryOrchestrator.Canonicalize).
public class ComparisonSummarySignatureTests
{
    private static DimensionScore Scored(int value, DateTimeOffset? publishedAt = null) =>
        new(new Score(value), DataFreshnessStatus.Fresh, "Test Source", publishedAt ?? DateTimeOffset.UtcNow);

    private static NeighborhoodScoreResult Result(string id, DimensionScore airQuality) => new(
        id, airQuality, Scored(70), Scored(60), Scored(50), Scored(40), Scored(30), new Score(50));

    [Fact]
    public void Compute_is_identical_when_only_freshness_metadata_differs_on_either_side_but_values_match()
    {
        var a1 = Result("kadikoy", Scored(80, DateTimeOffset.UtcNow));
        var a2 = Result("kadikoy", Scored(80, DateTimeOffset.UtcNow.AddDays(-30)));
        var b = Result("besiktas", Scored(60));

        Assert.Equal(ComparisonSummarySignature.Compute(a1, b), ComparisonSummarySignature.Compute(a2, b));
    }

    [Fact]
    public void Compute_differs_when_the_first_districts_value_changes()
    {
        var a1 = Result("kadikoy", Scored(80));
        var a2 = Result("kadikoy", Scored(81));
        var b = Result("besiktas", Scored(60));

        Assert.NotEqual(ComparisonSummarySignature.Compute(a1, b), ComparisonSummarySignature.Compute(a2, b));
    }

    [Fact]
    public void Compute_differs_when_the_second_districts_value_changes()
    {
        var a = Result("kadikoy", Scored(80));
        var b1 = Result("besiktas", Scored(60));
        var b2 = Result("besiktas", Scored(61));

        Assert.NotEqual(ComparisonSummarySignature.Compute(a, b1), ComparisonSummarySignature.Compute(a, b2));
    }

    // Documents WHY ComparisonSummaryOrchestrator must canonicalize (sort by id) before calling
    // this - without that, the same real pair compared in the opposite pick order would compute a
    // different signature and never share a cache hit.
    [Fact]
    public void Compute_is_sensitive_to_argument_order_so_callers_must_canonicalize_first()
    {
        var a = Result("kadikoy", Scored(80));
        var b = Result("besiktas", Scored(60));

        Assert.NotEqual(ComparisonSummarySignature.Compute(a, b), ComparisonSummarySignature.Compute(b, a));
    }
}

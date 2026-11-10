using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Summaries;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Tests.Summaries;

// DistrictSummaryGenerationJob relies entirely on this signature to decide whether a district's
// scores actually changed since the last generation - these tests prove it's stable when nothing
// meaningful changed (so a routine re-sync doesn't waste a Gemini call) and different when a real
// value does change or a dimension gains/loses data.
public class DistrictSummarySignatureTests
{
    private static DimensionScore Scored(int value, DateTimeOffset? publishedAt = null) =>
        new(new Score(value), DataFreshnessStatus.Fresh, "Test Source", publishedAt ?? DateTimeOffset.UtcNow);

    private static NeighborhoodScoreResult Result(
        DimensionScore airQuality, DimensionScore greenSpace, DimensionScore transportation,
        DimensionScore parking, DimensionScore healthAccess, DimensionScore transitAccess) =>
        new("kadikoy", airQuality, greenSpace, transportation, parking, healthAccess, transitAccess, new Score(50));

    [Fact]
    public void Compute_is_identical_when_only_freshness_or_source_metadata_differs_but_values_match()
    {
        var a = Result(Scored(80, DateTimeOffset.UtcNow), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));
        var b = Result(Scored(80, DateTimeOffset.UtcNow.AddDays(-30)), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));

        Assert.Equal(DistrictSummarySignature.Compute(a), DistrictSummarySignature.Compute(b));
    }

    [Fact]
    public void Compute_differs_when_a_dimension_value_changes()
    {
        var a = Result(Scored(80), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));
        var b = Result(Scored(81), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));

        Assert.NotEqual(DistrictSummarySignature.Compute(a), DistrictSummarySignature.Compute(b));
    }

    [Fact]
    public void Compute_differs_when_a_dimension_gains_data()
    {
        var a = Result(Scored(80), DimensionScore.NoData, Scored(60), Scored(50), Scored(40), Scored(30));
        var b = Result(Scored(80), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));

        Assert.NotEqual(DistrictSummarySignature.Compute(a), DistrictSummarySignature.Compute(b));
    }
}

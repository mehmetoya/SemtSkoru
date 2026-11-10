using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Trends;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Tests.Trends;

// ScoreSnapshotJob relies entirely on this to decide, with zero Gemini calls, whether there is
// anything real and meaningful to narrate for a district at all - these tests prove the two
// "never fabricate" boundaries that matter most: a delta below the noise threshold is dropped
// (not narrated as a trend), and a dimension missing on either side is never treated as a
// change to/from zero.
public class DistrictTrendDeltasTests
{
    private static readonly DateTimeOffset BaselineRecordedAt = DateTimeOffset.UtcNow.AddDays(-10);

    private static DimensionScore Scored(int value) =>
        new(new Score(value), DataFreshnessStatus.Fresh, "Test Source", DateTimeOffset.UtcNow);

    private static NeighborhoodScoreResult Current(
        DimensionScore airQuality, DimensionScore greenSpace, DimensionScore transportation,
        DimensionScore parking, DimensionScore healthAccess, DimensionScore transitAccess) =>
        new("kadikoy", airQuality, greenSpace, transportation, parking, healthAccess, transitAccess, new Score(50));

    private static ScoreSnapshotBaseline Baseline(
        int? airQuality, int? greenSpace, int? transportation, int? parking, int? healthAccess, int? transitAccess) =>
        new(BaselineRecordedAt, airQuality, greenSpace, transportation, parking, healthAccess, transitAccess, 50);

    [Fact]
    public void Compute_finds_a_real_delta_at_or_above_the_meaningful_threshold()
    {
        var baseline = Baseline(45, 70, 60, 50, 40, 30);
        var current = Current(Scored(48), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));

        var deltas = DistrictTrendDeltas.Compute(baseline, current);

        var delta = Assert.Single(deltas);
        Assert.Equal("airQuality", delta.Dimension);
        Assert.Equal(45, delta.PreviousScore);
        Assert.Equal(48, delta.CurrentScore);
        Assert.Equal(3, delta.Delta);
    }

    [Fact]
    public void Compute_drops_a_delta_below_the_meaningful_threshold()
    {
        // A 2-point move is real, but below MinimumMeaningfulDelta (3) - not noteworthy noise.
        var baseline = Baseline(45, 70, 60, 50, 40, 30);
        var current = Current(Scored(47), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));

        var deltas = DistrictTrendDeltas.Compute(baseline, current);

        Assert.Empty(deltas);
    }

    [Fact]
    public void Compute_never_treats_a_dimension_missing_from_the_baseline_as_a_change_from_zero()
    {
        // The dimension simply had no snapshot row at baseline time (see ScoreSnapshot's
        // remarks) - not a real zero. A large current score must never be reported as a huge
        // fabricated "increase from nothing".
        var baseline = Baseline(null, 70, 60, 50, 40, 30);
        var current = Current(Scored(90), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));

        var deltas = DistrictTrendDeltas.Compute(baseline, current);

        Assert.Empty(deltas);
    }

    [Fact]
    public void Compute_never_treats_a_dimension_that_lost_its_current_data_as_a_change_to_zero()
    {
        var baseline = Baseline(80, 70, 60, 50, 40, 30);
        var current = Current(DimensionScore.NoData, Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));

        var deltas = DistrictTrendDeltas.Compute(baseline, current);

        Assert.Empty(deltas);
    }

    [Fact]
    public void Compute_finds_every_dimension_that_meaningfully_changed_not_just_the_first()
    {
        var baseline = Baseline(45, 70, 60, 20, 40, 30);
        var current = Current(Scored(48), Scored(70), Scored(60), Scored(60), Scored(40), Scored(30));

        var deltas = DistrictTrendDeltas.Compute(baseline, current);

        Assert.Equal(2, deltas.Count);
        Assert.Contains(deltas, d => d.Dimension == "airQuality" && d.Delta == 3);
        Assert.Contains(deltas, d => d.Dimension == "parking" && d.Delta == 40);
    }

    [Fact]
    public void Compute_returns_empty_when_nothing_changed_at_all()
    {
        var baseline = Baseline(45, 70, 60, 50, 40, 30);
        var current = Current(Scored(45), Scored(70), Scored(60), Scored(50), Scored(40), Scored(30));

        var deltas = DistrictTrendDeltas.Compute(baseline, current);

        Assert.Empty(deltas);
    }
}

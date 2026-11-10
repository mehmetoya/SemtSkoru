using SemtSkoru.Application.Trends;

namespace SemtSkoru.Application.Tests.Trends;

// ScoreSnapshotJob relies entirely on this signature to decide whether a district's comparison
// basis (which dimensions changed, and by how much) has actually changed since the last trend
// text was generated - these tests prove it's stable when the same real deltas recur (so a
// re-run doesn't waste a Gemini call) and different whenever a real value or a real dimension
// set changes, while staying independent of ordering.
public class DistrictTrendSignatureTests
{
    [Fact]
    public void Compute_is_identical_for_the_same_deltas_regardless_of_order()
    {
        var a = new[]
        {
            new DimensionDelta("airQuality", 45, 80),
            new DimensionDelta("parking", 20, 60),
        };
        var b = new[]
        {
            new DimensionDelta("parking", 20, 60),
            new DimensionDelta("airQuality", 45, 80),
        };

        Assert.Equal(DistrictTrendSignature.Compute(a), DistrictTrendSignature.Compute(b));
    }

    [Fact]
    public void Compute_differs_when_a_deltas_current_score_changes()
    {
        var a = new[] { new DimensionDelta("parking", 20, 60) };
        var b = new[] { new DimensionDelta("parking", 20, 65) };

        Assert.NotEqual(DistrictTrendSignature.Compute(a), DistrictTrendSignature.Compute(b));
    }

    [Fact]
    public void Compute_differs_when_the_set_of_meaningfully_changed_dimensions_differs()
    {
        var a = new[] { new DimensionDelta("parking", 20, 60) };
        var b = new[] { new DimensionDelta("parking", 20, 60), new DimensionDelta("airQuality", 45, 80) };

        Assert.NotEqual(DistrictTrendSignature.Compute(a), DistrictTrendSignature.Compute(b));
    }

    [Fact]
    public void Compute_is_empty_string_for_no_deltas()
    {
        Assert.Equal(string.Empty, DistrictTrendSignature.Compute([]));
    }
}

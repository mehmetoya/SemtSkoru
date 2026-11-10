using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Tests;

public class DimensionScoringTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(-10, 100)] // clamps below range
    [InlineData(50, 83)] // top of EPA "Good"
    [InlineData(100, 67)] // top of EPA "Moderate"
    [InlineData(150, 50)] // top of EPA "Unhealthy for Sensitive Groups"
    [InlineData(200, 33)] // top of EPA "Unhealthy"
    [InlineData(300, 17)] // top of EPA "Very Unhealthy"
    [InlineData(500, 0)] // top of EPA "Hazardous"
    [InlineData(700, 0)] // clamps above range
    public void ScoreAirQuality_maps_EPA_AQI_bands_onto_descending_score_bands(double aqi, int expectedScore)
    {
        Assert.Equal(expectedScore, DimensionScoring.ScoreAirQuality(aqi).Value);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1000, 50)]
    [InlineData(2000, 0)]
    [InlineData(5000, 0)] // clamps beyond the walkable range
    [InlineData(-100, 100)] // clamps below range (not physically possible, but the formula should still saturate rather than extrapolate)
    public void ScoreGreenSpace_scores_closer_parks_higher(double distanceMeters, int expectedScore)
    {
        Assert.Equal(expectedScore, DimensionScoring.ScoreGreenSpace(distanceMeters).Value);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 50)]
    [InlineData(50, 100)]
    [InlineData(90, 100)] // clamps above free-flow speed
    [InlineData(-10, 0)] // clamps below range (not physically possible, but the formula should still saturate rather than extrapolate)
    public void ScoreTransportation_scores_faster_average_speed_higher(double speedKmh, int expectedScore)
    {
        Assert.Equal(expectedScore, DimensionScoring.ScoreTransportation(speedKmh).Value);
    }

    [Fact]
    public void ScoreTransportation_rounds_an_exact_midpoint_to_even_not_up()
    {
        // 22.25/50*100 = 44.5 exactly. .NET's Math.Round with no explicit mode defaults to
        // MidpointRounding.ToEven ("banker's rounding"), so this rounds DOWN to 44 (the even
        // neighbor), not up to 45 as naive "round half up" intuition would suggest. Locking
        // this in explicitly - every dimension formula shares this same rounding behavior via
        // DimensionScoring.ToScore, and no other test happens to land on a genuine odd/even
        // midpoint, so this was previously untested and easy to mistake for an off-by-one bug.
        Assert.Equal(44, DimensionScoring.ScoreTransportation(22.25).Value);
    }

    [Theory]
    [InlineData(0, 0)] // entirely full
    [InlineData(0.5, 50)]
    [InlineData(1, 100)] // entirely empty
    [InlineData(-0.2, 0)] // clamps below range
    [InlineData(1.2, 100)] // clamps above range (a stale/inconsistent record)
    public void ScoreParking_scores_higher_availability_higher(double averageAvailabilityRatio, int expectedScore)
    {
        Assert.Equal(expectedScore, DimensionScoring.ScoreParking(averageAvailabilityRatio).Value);
    }

    [Theory]
    [InlineData(10.30, 0)] // observed worst real district (Şile)
    [InlineData(79.72, 100)] // observed best real district (Fatih)
    [InlineData(45.01, 50)] // midpoint of the observed range
    [InlineData(0, 0)] // clamps below the observed range
    [InlineData(100, 100)] // clamps above the observed range
    public void ScoreHealthAccess_scores_higher_weighted_index_higher(double weightedHealthIndex, int expectedScore)
    {
        Assert.Equal(expectedScore, DimensionScoring.ScoreHealthAccess(weightedHealthIndex).Value);
    }

    [Theory]
    [InlineData(0.28, 0)] // observed worst real district (Çatalca)
    [InlineData(20.13, 100)] // observed best real district (Şişli)
    [InlineData(10.205, 50)] // midpoint of the observed range
    [InlineData(0, 0)] // clamps below the observed range
    [InlineData(50, 100)] // clamps above the observed range
    public void ScoreTransitAccess_scores_higher_stop_density_higher(double stopDensityPerKm2, int expectedScore)
    {
        Assert.Equal(expectedScore, DimensionScoring.ScoreTransitAccess(stopDensityPerKm2).Value);
    }
}

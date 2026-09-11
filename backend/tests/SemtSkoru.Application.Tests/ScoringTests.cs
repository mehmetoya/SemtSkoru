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
    public void ScoreGreenSpace_scores_closer_parks_higher(double distanceMeters, int expectedScore)
    {
        Assert.Equal(expectedScore, DimensionScoring.ScoreGreenSpace(distanceMeters).Value);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 50)]
    [InlineData(50, 100)]
    [InlineData(90, 100)] // clamps above free-flow speed
    public void ScoreTransportation_scores_faster_average_speed_higher(double speedKmh, int expectedScore)
    {
        Assert.Equal(expectedScore, DimensionScoring.ScoreTransportation(speedKmh).Value);
    }
}

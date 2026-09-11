using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

/// <summary>
/// Pure 0-100 scoring functions for each raw metric. Higher score always means "more livable."
/// </summary>
public static class DimensionScoring
{
    // AQIIndex from İBB's air quality API follows the standard EPA breakpoints (0-500,
    // verified live against api.ibb.gov.tr: an AQIIndex of ~27 came back with State "memnun
    // edici" / Color #13a261, matching the EPA "Good" band). Each EPA category is mapped onto
    // an equal 100/6 slice of the score range, worst category first, so the score reflects the
    // same health-risk bands the source itself categorizes air quality into.
    private static readonly (double AqiLow, double AqiHigh, double ScoreHigh, double ScoreLow)[] AirQualityBands =
    [
        (0, 50, 100, 250d / 3),
        (50, 100, 250d / 3, 200d / 3),
        (100, 150, 200d / 3, 50),
        (150, 200, 50, 100d / 3),
        (200, 300, 100d / 3, 50d / 3),
        (300, 500, 50d / 3, 0),
    ];

    public static Score ScoreAirQuality(double aqiIndex)
    {
        var clamped = Math.Clamp(aqiIndex, 0, 500);
        var (aqiLow, aqiHigh, scoreHigh, scoreLow) = Array.Find(AirQualityBands, b => clamped <= b.AqiHigh);
        var score = InterpolateDescending(clamped, aqiLow, aqiHigh, scoreHigh, scoreLow);
        return ToScore(score);
    }

    // No official standard for park-distance livability; MVP treats anything within a
    // ~25 minute walk (2 km) as linearly better the closer it is, zero beyond that.
    private const double MaxWalkableDistanceMeters = 2000;

    public static Score ScoreGreenSpace(double nearestParkDistanceMeters)
    {
        var clamped = Math.Clamp(nearestParkDistanceMeters, 0, MaxWalkableDistanceMeters);
        var score = InterpolateDescending(clamped, 0, MaxWalkableDistanceMeters, 100, 0);
        return ToScore(score);
    }

    // Istanbul's in-city speed limit is 50 km/h; treat free-flowing traffic at or above that
    // as a perfect score and gridlock (0 km/h) as zero, linear between.
    private const double FreeFlowSpeedKmh = 50;

    public static Score ScoreTransportation(double averageSpeedKmh)
    {
        var clamped = Math.Clamp(averageSpeedKmh, 0, FreeFlowSpeedKmh);
        var score = clamped / FreeFlowSpeedKmh * 100;
        return ToScore(score);
    }

    private static double InterpolateDescending(double value, double fromLow, double fromHigh, double toHigh, double toLow) =>
        toHigh + (value - fromLow) / (fromHigh - fromLow) * (toLow - toHigh);

    private static Score ToScore(double value) => new(Math.Clamp((int)Math.Round(value), 0, 100));
}

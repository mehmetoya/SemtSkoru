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

    // İSPARK publishes no standard for "good" parking availability, so MVP treats the raw
    // average available-capacity ratio (emptyCapacity/capacity, averaged unweighted across a
    // district's facilities - see ParkingIngestionJob) as directly proportional to livability:
    // facilities that are, on average, entirely empty score 100 (most available), entirely full
    // score 0. This is a small, point-in-time sample of off-street İSPARK facilities only - many
    // districts have just one or two - and says nothing about on-street parking, which İSPARK
    // does not track, so it should be read as a rough proxy rather than a precise measure.
    public static Score ScoreParking(double averageAvailabilityRatio)
    {
        var clamped = Math.Clamp(averageAvailabilityRatio, 0, 1);
        var score = clamped * 100;
        return ToScore(score);
    }

    // İBB's "34 Dakika İstanbul Sağlık İndeksi" (health-service access index, part of the
    // wider "34 Dakika İstanbul" Diversity Index's "Health" function - see
    // docs/data-sources.md) documents no claim that its raw SAGLIK_INDEX is itself a 0-100
    // scale; only the combined Diversity+Affordability+Walkability "Quality of Life Index" is
    // (per İBB's own methodology PDF). The raw mahalle-level index ranges 0-82.9 city-wide, but
    // HealthAccessIngestionJob aggregates it up to a population-weighted district average
    // first, which live-verified (2026-09-13) real data across all 39 districts narrows to
    // roughly 10.30 (Şile) - 79.72 (Fatih). MVP linearly rescales that *observed* district-
    // level range to 0-100 (worst real district = 0, best real district = 100) - a documented,
    // hedged assumption like ScoreGreenSpace's walking-distance cutoff, not an official
    // standard, so a future re-fetch that shifts the true extremes would shift every district's
    // score proportionally rather than silently going out of range.
    private const double MinObservedWeightedHealthIndex = 10.30; // Şile
    private const double MaxObservedWeightedHealthIndex = 79.72; // Fatih

    public static Score ScoreHealthAccess(double weightedHealthIndex)
    {
        var clamped = Math.Clamp(weightedHealthIndex, MinObservedWeightedHealthIndex, MaxObservedWeightedHealthIndex);
        var score = (clamped - MinObservedWeightedHealthIndex)
            / (MaxObservedWeightedHealthIndex - MinObservedWeightedHealthIndex) * 100;
        return ToScore(score);
    }

    // İETT publishes no standard for "how many bus stops per km² is good", so, like
    // ScoreHealthAccess, MVP linearly rescales the *observed* district-level range to 0-100
    // (worst real district = 0, best real district = 100) rather than inventing an absolute
    // threshold. Live-verified (2026-09-13) real data across all 39 districts: TransitAccessIngestionJob's
    // stops-per-km² (equirectangular area approximation, not a raw stop count - a large rural
    // district would otherwise be unfairly rewarded/penalized just for its size) ranges from
    // 0.28 (Çatalca, a large sparsely-covered western district) to 20.13 (Şişli, a small dense
    // central one). Like ScoreHealthAccess's bounds, a future re-fetch that shifts the true
    // extremes would shift every district's score proportionally rather than silently going out
    // of range.
    private const double MinObservedStopDensityPerKm2 = 0.28; // Çatalca
    private const double MaxObservedStopDensityPerKm2 = 20.13; // Şişli

    public static Score ScoreTransitAccess(double stopDensityPerKm2)
    {
        var clamped = Math.Clamp(stopDensityPerKm2, MinObservedStopDensityPerKm2, MaxObservedStopDensityPerKm2);
        var score = (clamped - MinObservedStopDensityPerKm2)
            / (MaxObservedStopDensityPerKm2 - MinObservedStopDensityPerKm2) * 100;
        return ToScore(score);
    }

    private static double InterpolateDescending(double value, double fromLow, double fromHigh, double toHigh, double toLow) =>
        toHigh + (value - fromLow) / (fromHigh - fromLow) * (toLow - toHigh);

    private static Score ToScore(double value) => new(Math.Clamp((int)Math.Round(value), 0, 100));
}

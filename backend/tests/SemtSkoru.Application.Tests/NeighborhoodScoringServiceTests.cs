using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Tests;

public class NeighborhoodScoringServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static DataSourceMetadata LiveSource(DateTimeOffset publishedAt) => new(
        SourceName: "İBB Hava Kalitesi",
        SourceUrl: "https://api.ibb.gov.tr/havakalitesi",
        SourceLicense: "İBB Açık Veri Lisansı",
        FetchedAt: publishedAt,
        PublishedAt: publishedAt,
        LastSuccessfulSyncAt: publishedAt,
        Cadence: SourceCadence.Live);

    private static NeighborhoodScoringService CreateService(INeighborhoodScoringRepository repository) =>
        new(repository, new FakeTimeProvider(Now));

    [Fact]
    public async Task GetScoreAsync_returns_null_when_the_neighborhood_does_not_exist()
    {
        var repository = new FakeScoringRepository(exists: false);

        var result = await CreateService(repository).GetScoreAsync("nonexistent", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetScoreAsync_scores_every_dimension_and_averages_them_into_an_overall_score()
    {
        var repository = new FakeScoringRepository(exists: true)
        {
            AirQuality = new AirQualityReading { NeighborhoodId = "kadikoy", AqiIndex = 50, Source = LiveSource(Now) }, // -> 83
            GreenSpace = new GreenSpaceReading { NeighborhoodId = "kadikoy", NearestParkDistanceMeters = 0, Source = LiveSource(Now) }, // -> 100
            Traffic = new TrafficReading { NeighborhoodId = "kadikoy", AverageSpeedKmh = 50, Source = LiveSource(Now) }, // -> 100
            Parking = new ParkingReading { NeighborhoodId = "kadikoy", AverageAvailabilityRatio = 1.0, Source = LiveSource(Now) }, // -> 100
            HealthAccess = new HealthAccessReading { NeighborhoodId = "kadikoy", WeightedHealthIndex = 79.72, Source = LiveSource(Now) }, // -> 100
            TransitAccess = new TransitAccessReading { NeighborhoodId = "kadikoy", StopCount = 10, StopDensityPerKm2 = 20.13, Source = LiveSource(Now) }, // -> 100
        };

        var result = await CreateService(repository).GetScoreAsync("kadikoy", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsComplete);
        Assert.Equal(83, result.AirQuality.Value!.Value.Value);
        Assert.Equal(100, result.GreenSpace.Value!.Value.Value);
        Assert.Equal(100, result.Transportation.Value!.Value.Value);
        Assert.Equal(100, result.Parking.Value!.Value.Value);
        Assert.Equal(100, result.HealthAccess.Value!.Value.Value);
        Assert.Equal(100, result.TransitAccess.Value!.Value.Value);
        Assert.Equal(97, result.Overall!.Value.Value); // round((83+100+100+100+100+100)/6) == round(97.16) == 97
        Assert.Equal("İBB Hava Kalitesi", result.AirQuality.SourceName);
        Assert.Equal(Now, result.AirQuality.PublishedAt);
    }

    [Fact]
    public async Task GetScoreAsync_marks_a_missing_dimension_as_no_data_without_crashing_and_averages_the_rest()
    {
        var repository = new FakeScoringRepository(exists: true)
        {
            AirQuality = new AirQualityReading { NeighborhoodId = "besiktas", AqiIndex = 0, Source = LiveSource(Now) }, // -> 100
            GreenSpace = null, // no park found for this district yet
            Traffic = new TrafficReading { NeighborhoodId = "besiktas", AverageSpeedKmh = 50, Source = LiveSource(Now) }, // -> 100
            Parking = new ParkingReading { NeighborhoodId = "besiktas", AverageAvailabilityRatio = 1.0, Source = LiveSource(Now) }, // -> 100
            HealthAccess = new HealthAccessReading { NeighborhoodId = "besiktas", WeightedHealthIndex = 79.72, Source = LiveSource(Now) }, // -> 100
            TransitAccess = new TransitAccessReading { NeighborhoodId = "besiktas", StopCount = 5, StopDensityPerKm2 = 20.13, Source = LiveSource(Now) }, // -> 100
        };

        var result = await CreateService(repository).GetScoreAsync("besiktas", CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsComplete);
        Assert.False(result.GreenSpace.HasData);
        Assert.Null(result.GreenSpace.Value);
        Assert.Null(result.GreenSpace.Freshness);
        Assert.Null(result.GreenSpace.SourceName);
        Assert.Null(result.GreenSpace.PublishedAt);
        Assert.Equal(100, result.Overall!.Value.Value); // average of the five available dimensions
    }

    [Fact]
    public async Task GetScoreAsync_returns_a_null_overall_when_no_dimension_has_data_at_all()
    {
        var repository = new FakeScoringRepository(exists: true);

        var result = await CreateService(repository).GetScoreAsync("uskudar", CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsComplete);
        Assert.Null(result.Overall);
    }

    [Fact]
    public async Task GetScoreAsync_still_scores_a_stale_reading_but_flags_its_freshness()
    {
        var staleSince = Now - TimeSpan.FromDays(30); // well past the 7-day Live threshold
        var repository = new FakeScoringRepository(exists: true)
        {
            AirQuality = new AirQualityReading { NeighborhoodId = "kadikoy", AqiIndex = 0, Source = LiveSource(staleSince) },
        };

        var result = await CreateService(repository).GetScoreAsync("kadikoy", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.AirQuality.HasData);
        Assert.Equal(100, result.AirQuality.Value!.Value.Value);
        Assert.Equal(DataFreshnessStatus.Stale, result.AirQuality.Freshness);
    }

    [Fact]
    public async Task GetAllScoresAsync_scores_every_neighborhood_from_the_bulk_lookups()
    {
        var repository = new FakeScoringRepository(exists: true)
        {
            AllIds = ["kadikoy", "besiktas"],
            AllAirQuality = new Dictionary<string, AirQualityReading>
            {
                ["kadikoy"] = new() { NeighborhoodId = "kadikoy", AqiIndex = 50, Source = LiveSource(Now) }, // -> 83
            },
            AllGreenSpace = new Dictionary<string, GreenSpaceReading>
            {
                ["kadikoy"] = new() { NeighborhoodId = "kadikoy", NearestParkDistanceMeters = 0, Source = LiveSource(Now) }, // -> 100
            },
            AllTraffic = new Dictionary<string, TrafficReading>(),
            AllParking = new Dictionary<string, ParkingReading>(),
            AllHealthAccess = new Dictionary<string, HealthAccessReading>(),
            AllTransitAccess = new Dictionary<string, TransitAccessReading>(),
        };

        var results = await CreateService(repository).GetAllScoresAsync(CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.True(results["kadikoy"].IsComplete == false); // no traffic data
        Assert.Equal(92, results["kadikoy"].Overall!.Value.Value); // round((83+100)/2)
        Assert.False(results["besiktas"].AirQuality.HasData);
        Assert.Null(results["besiktas"].Overall);
    }
}

file sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

file sealed class FakeScoringRepository(bool exists) : INeighborhoodScoringRepository
{
    public AirQualityReading? AirQuality { get; set; }
    public GreenSpaceReading? GreenSpace { get; set; }
    public TrafficReading? Traffic { get; set; }
    public ParkingReading? Parking { get; set; }
    public HealthAccessReading? HealthAccess { get; set; }
    public TransitAccessReading? TransitAccess { get; set; }

    public IReadOnlyList<string> AllIds { get; set; } = [];
    public IReadOnlyDictionary<string, AirQualityReading> AllAirQuality { get; set; } = new Dictionary<string, AirQualityReading>();
    public IReadOnlyDictionary<string, GreenSpaceReading> AllGreenSpace { get; set; } = new Dictionary<string, GreenSpaceReading>();
    public IReadOnlyDictionary<string, TrafficReading> AllTraffic { get; set; } = new Dictionary<string, TrafficReading>();
    public IReadOnlyDictionary<string, ParkingReading> AllParking { get; set; } = new Dictionary<string, ParkingReading>();
    public IReadOnlyDictionary<string, HealthAccessReading> AllHealthAccess { get; set; } = new Dictionary<string, HealthAccessReading>();
    public IReadOnlyDictionary<string, TransitAccessReading> AllTransitAccess { get; set; } = new Dictionary<string, TransitAccessReading>();

    public Task<bool> NeighborhoodExistsAsync(string neighborhoodId, CancellationToken ct) => Task.FromResult(exists);

    public Task<AirQualityReading?> GetLatestAirQualityAsync(string neighborhoodId, CancellationToken ct) => Task.FromResult(AirQuality);

    public Task<GreenSpaceReading?> GetLatestGreenSpaceAsync(string neighborhoodId, CancellationToken ct) => Task.FromResult(GreenSpace);

    public Task<TrafficReading?> GetLatestTrafficAsync(string neighborhoodId, CancellationToken ct) => Task.FromResult(Traffic);

    public Task<ParkingReading?> GetLatestParkingAsync(string neighborhoodId, CancellationToken ct) => Task.FromResult(Parking);

    public Task<HealthAccessReading?> GetLatestHealthAccessAsync(string neighborhoodId, CancellationToken ct) => Task.FromResult(HealthAccess);

    public Task<TransitAccessReading?> GetLatestTransitAccessAsync(string neighborhoodId, CancellationToken ct) => Task.FromResult(TransitAccess);

    public Task<IReadOnlyList<string>> GetAllNeighborhoodIdsAsync(CancellationToken ct) => Task.FromResult(AllIds);

    public Task<IReadOnlyDictionary<string, AirQualityReading>> GetAllAirQualityAsync(CancellationToken ct) => Task.FromResult(AllAirQuality);

    public Task<IReadOnlyDictionary<string, GreenSpaceReading>> GetAllGreenSpaceAsync(CancellationToken ct) => Task.FromResult(AllGreenSpace);

    public Task<IReadOnlyDictionary<string, TrafficReading>> GetAllTrafficAsync(CancellationToken ct) => Task.FromResult(AllTraffic);

    public Task<IReadOnlyDictionary<string, ParkingReading>> GetAllParkingAsync(CancellationToken ct) => Task.FromResult(AllParking);

    public Task<IReadOnlyDictionary<string, HealthAccessReading>> GetAllHealthAccessAsync(CancellationToken ct) => Task.FromResult(AllHealthAccess);

    public Task<IReadOnlyDictionary<string, TransitAccessReading>> GetAllTransitAccessAsync(CancellationToken ct) => Task.FromResult(AllTransitAccess);
}

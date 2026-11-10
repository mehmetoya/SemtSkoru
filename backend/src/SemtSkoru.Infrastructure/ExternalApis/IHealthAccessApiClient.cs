namespace SemtSkoru.Infrastructure.ExternalApis;

public sealed record HealthWardFeatureDto(string District, string WardName, double HealthIndex, int Population);

public interface IHealthAccessApiClient
{
    /// <summary>
    /// Returns every mahalle (neighborhood-within-district) feature from İBB's "34 Dakika
    /// İstanbul Sağlık İndeksi" (health-service access index) dataset, city-wide, with its
    /// own district tag, raw health index value, and population.
    /// </summary>
    Task<IReadOnlyList<HealthWardFeatureDto>> GetWardsAsync(CancellationToken ct);
}

using SemtSkoru.Domain;

namespace SemtSkoru.Application.Summaries;

/// <summary>
/// Read-only access to the cached DistrictSummary rows DistrictSummaryGenerationJob writes.
/// Deliberately separate from that job's own write path (which goes through AppDbContext
/// directly in Infrastructure, matching every other ingestion job's style) - this is only the
/// cheap read side the API endpoints need, kept as an interface here so it's fakeable in tests.
/// </summary>
public interface IDistrictSummaryRepository
{
    Task<DistrictSummary?> GetAsync(string neighborhoodId, CancellationToken ct);
}

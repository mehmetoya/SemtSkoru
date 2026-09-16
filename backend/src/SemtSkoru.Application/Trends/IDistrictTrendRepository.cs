using SemtSkoru.Domain;

namespace SemtSkoru.Application.Trends;

/// <summary>
/// Read-only access to the cached DistrictTrendSummary rows ScoreSnapshotJob writes. Deliberately
/// separate from that job's own write path (which goes through AppDbContext directly in
/// Infrastructure, matching every other ingestion job's style) - this is only the cheap read side
/// the API endpoint needs, kept as an interface here so it's fakeable in tests. Mirrors
/// IDistrictSummaryRepository exactly.
/// </summary>
public interface IDistrictTrendRepository
{
    /// <summary>Reads the cached trend summary for this (district, locale) pair - "tr"/"en", see
    /// SemtSkoru.Application.Localization.AiLocale. Mirrors IDistrictSummaryRepository.GetAsync's
    /// own locale-scoping exactly: never falls back to the other locale's row.</summary>
    Task<DistrictTrendSummary?> GetAsync(string neighborhoodId, string locale, CancellationToken ct);
}

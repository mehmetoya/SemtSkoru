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
    /// <summary>Reads the cached summary for this (district, locale) pair - "tr"/"en", see
    /// SemtSkoru.Application.Localization.AiLocale. Never falls back to the other locale: an
    /// "en" row genuinely not existing yet (e.g. the weekly job hasn't caught up since this app
    /// added locale support) must read as null, not silently serve Turkish prose under English
    /// UI chrome - that mismatch is exactly the bug locale-awareness exists to fix.</summary>
    Task<DistrictSummary?> GetAsync(string neighborhoodId, string locale, CancellationToken ct);
}

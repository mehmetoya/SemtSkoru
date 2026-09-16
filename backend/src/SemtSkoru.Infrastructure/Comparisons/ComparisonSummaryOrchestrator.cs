using Microsoft.EntityFrameworkCore;
using SemtSkoru.Application.Comparisons;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Comparisons;

/// <summary>
/// Implements the hybrid on-demand-generation-plus-persistent-cache flow described on
/// IComparisonSummaryOrchestrator. Called synchronously from the compare-summary HTTP endpoint
/// (see NeighborhoodEndpoints.cs), NOT from a Hangfire job - unlike DistrictSummaryGenerationJob,
/// which can afford to pre-generate all 39 districts' summaries weekly (39 rows is cheap),
/// pre-generating all 39-choose-2 = 741 possible district PAIRS weekly would be ~19x that job's
/// worst-case Gemini spend for a feature most pairs will never actually be viewed for. So instead:
///
///  - A repeat view of a pair whose underlying scores haven't changed is a single indexed-by-
///    primary-key DB read (this class's own db.ComparisonSummaries.FindAsync below) - no Gemini
///    call, same "compute once, serve a cheap read" property DistrictSummaryGenerationJob already
///    gives every OTHER district-scoped AI feature in this app, just triggered lazily per pair
///    instead of eagerly for all of them.
///  - A pair that's never been compared before (or whose cached signature no longer matches
///    either district's CURRENT scores) costs exactly one live Gemini call, made directly on this
///    request - the caller (the HTTP endpoint) is expected to be on the SAME shared, rate-limited
///    Gemini budget as the AI Semt Asistanı (RateLimitPolicies.AiAssistant - see
///    RateLimiting/RateLimitingExtensions.cs), not a separate one, since this app's whole Gemini
///    free-tier quota (150 requests/day, ~5/min) is shared across every AI feature at once.
///
/// A generation failure of ANY kind (no key configured, Gemini down/rate-limited/timed out, or an
/// unusable model response) never throws and never persists a placeholder - it returns null, and
/// the caller is expected to simply omit the AI summary from the response, exactly like
/// DistrictSummaryBadge already does for a null district summary.
/// </summary>
public sealed class ComparisonSummaryOrchestrator(
    IComparisonSummaryService summaryService,
    AppDbContext db,
    TimeProvider timeProvider) : IComparisonSummaryOrchestrator
{
    public async Task<ComparisonSummary?> GetOrGenerateAsync(
        string neighborhoodNameA, NeighborhoodScoreResult scoreA,
        string neighborhoodNameB, NeighborhoodScoreResult scoreB,
        CancellationToken ct)
    {
        var (idLo, nameLo, scoreLo, idHi, nameHi, scoreHi) =
            Canonicalize(neighborhoodNameA, scoreA, neighborhoodNameB, scoreB);

        var signature = ComparisonSummarySignature.Compute(scoreLo, scoreHi);

        var existing = await db.ComparisonSummaries.FindAsync([idLo, idHi], ct);
        if (existing is not null && existing.ScoreSignature == signature)
        {
            return existing; // cache hit: neither district's scores changed since this was written.
        }

        var outcome = await summaryService.GenerateComparisonAsync(nameLo, scoreLo, nameHi, scoreHi, ct);
        if (outcome.Kind != ComparisonSummaryOutcomeKind.Ok)
        {
            // Deliberately does NOT fall back to a stale `existing` row here even though one may
            // exist: its text was grounded in scores that are now known to be out of date (that's
            // exactly why we got this far instead of taking the cache-hit branch above), and
            // showing it next to the freshly-rendered CURRENT scores could describe a comparison
            // that no longer holds - an honest "no summary this time" is safer than a possibly-
            // stale one, matching this app's "never show something uncertain as if it were solid"
            // principle.
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (existing is null)
        {
            existing = new ComparisonSummary
            {
                NeighborhoodIdA = idLo,
                NeighborhoodIdB = idHi,
                SummaryText = outcome.SummaryText!,
                GeneratedAt = now,
                ScoreSignature = signature,
            };
            db.ComparisonSummaries.Add(existing);
        }
        else
        {
            existing.SummaryText = outcome.SummaryText!;
            existing.GeneratedAt = now;
            existing.ScoreSignature = signature;
        }

        await db.SaveChangesAsync(ct);
        return existing;
    }

    // Sorts the pair by real neighborhood id (ordinal) so the SAME pair - regardless of which
    // district a visitor picked as "first"/"second" on /karsilastir - always maps to the same
    // cache row and the same combined signature. Without this, comparing (kadikoy, besiktas) and
    // (besiktas, kadikoy) would silently create two separate cache rows for what is, to a user,
    // the exact same comparison.
    private static (string IdLo, string NameLo, NeighborhoodScoreResult ScoreLo,
        string IdHi, string NameHi, NeighborhoodScoreResult ScoreHi) Canonicalize(
        string nameA, NeighborhoodScoreResult scoreA, string nameB, NeighborhoodScoreResult scoreB) =>
        string.CompareOrdinal(scoreA.NeighborhoodId, scoreB.NeighborhoodId) <= 0
            ? (scoreA.NeighborhoodId, nameA, scoreA, scoreB.NeighborhoodId, nameB, scoreB)
            : (scoreB.NeighborhoodId, nameB, scoreB, scoreA.NeighborhoodId, nameA, scoreA);
}

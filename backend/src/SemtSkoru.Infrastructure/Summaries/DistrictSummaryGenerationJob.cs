using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Summaries;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Summaries;

/// <summary>
/// Generates the AI "standout traits" summary shown on each district's page (see
/// DistrictSummaryService and web/components/NeighborhoodScoreCard.tsx) for all 39 districts, on
/// its own schedule - see this job's Hangfire registration in Program.cs for the weekly cadence
/// and the Gemini-quota math behind it. This exists specifically so a page view NEVER triggers a
/// live Gemini call: the API's /score endpoint only ever reads whatever this job already wrote
/// (DistrictSummaryRepository), the same "compute once in the background, serve a cheap read"
/// split every other dimension in this app already uses for its own ingestion.
///
/// Two things this job is solely responsible for, because nothing else in the app does either:
///
///  - PACING its own outbound Gemini calls. ASP.NET Core's rate limiter
///    (RateLimiting/RateLimitingExtensions.cs) only guards INBOUND HTTP requests to this API - it
///    has no idea this job exists and does nothing to slow down its calls to Gemini. Left
///    unpaced, one run of this job would fire up to 39 requests back to back, which is far above
///    even the higher end of the free tier's publicly-reported per-minute figures
///    (GeminiClient.cs/RateLimitingExtensions.cs cite ~5-15 RPM) and would also crowd out
///    whatever headroom the interactive AI Semt Asistanı needs from that SAME shared quota at
///    that moment.
///
///  - SKIPPING districts whose scores haven't actually changed (DistrictSummarySignature), so a
///    weekly run's real Gemini spend is usually far below the 39-calls-worst-case figure the
///    cadence below is sized against.
/// </summary>
public sealed class DistrictSummaryGenerationJob(
    INeighborhoodDirectory directory,
    INeighborhoodScoringService scoringService,
    IDistrictSummaryService summaryService,
    AppDbContext db,
    ILogger<DistrictSummaryGenerationJob> logger,
    TimeProvider timeProvider)
{
    // One call every 5 seconds -> 12/minute, comfortably under even the LOW end of the
    // publicly-reported free-tier RPM figures this app already budgets against elsewhere
    // (RateLimitingExtensions.cs: "roughly 15 requests/minute... sized as a conservative fraction
    // of the LOWEST figure found"), while still leaving room in that same per-minute window for
    // the interactive AI Semt Asistanı's own calls (capped separately at 5/minute) to land
    // without the two features colliding. Worst case (every one of the 39 districts needs
    // regenerating) this run takes ~39 x 5s ≈ 3.5 minutes wall-clock - a trivial cost for a job
    // that runs once a week, and it is bounded above by AiAssistantGlobalPermitLimitPerDay (150)
    // regardless, since even a full 39-call run is well under a third of one day's shared budget.
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromSeconds(5);

    // If Gemini itself says slow down mid-run, back off far longer than the routine per-district
    // pacing above before trying the next district - looping at the normal 5s cadence right after
    // a 429 would almost certainly just collect 38 more of them.
    private static readonly TimeSpan RateLimitBackoff = TimeSpan.FromSeconds(30);

    public async Task RunAsync(CancellationToken ct)
    {
        var names = await directory.GetAllNamesAsync(ct);
        var scores = await scoringService.GetAllScoresAsync(ct);
        var existingSummaries = await db.DistrictSummaries.ToDictionaryAsync(s => s.NeighborhoodId, ct);

        var now = timeProvider.GetUtcNow();
        var isFirstCall = true;

        foreach (var (id, name) in names.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            // Neither branch spends a Gemini call or the pacing delay below: a district that
            // doesn't exist in the bulk result at all, or exists but has had nothing ingested for
            // any of its six dimensions yet, has nothing for the AI to summarize - asking it to
            // describe six nulls would be a wasted call at best and an invitation to fabricate at
            // worst (see DistrictSummaryService.HasAnyData/InsufficientData).
            if (!scores.TryGetValue(id, out var score) || !score.HasAnyData)
            {
                continue;
            }

            var signature = DistrictSummarySignature.Compute(score);
            var existing = existingSummaries.GetValueOrDefault(id);
            if (existing is not null && existing.ScoreSignature == signature)
            {
                logger.LogInformation(
                    "Skipping district summary for {NeighborhoodId}: scores unchanged since {GeneratedAt}",
                    id, existing.GeneratedAt);
                continue; // nothing about the numbers changed since the last generation - don't spend a Gemini call.
            }

            if (!isFirstCall)
            {
                await Task.Delay(ThrottleDelay, ct);
            }
            isFirstCall = false;

            var outcome = await summaryService.GenerateSummaryAsync(name, score, ct);

            if (outcome.Kind == DistrictSummaryOutcomeKind.NotConfigured)
            {
                logger.LogInformation("Gemini API key not configured; skipping district summary generation entirely");
                return; // no key in this environment - every remaining district would fail identically.
            }

            if (outcome.Kind == DistrictSummaryOutcomeKind.RateLimited)
            {
                logger.LogWarning("Gemini rate-limited district summary generation at {NeighborhoodId}; backing off", id);
                await Task.Delay(RateLimitBackoff, ct);
                continue;
            }

            if (outcome.Kind != DistrictSummaryOutcomeKind.Ok)
            {
                logger.LogInformation("No usable district summary generated for {NeighborhoodId}: {Outcome}", id, outcome.Kind);
                continue;
            }

            if (existing is null)
            {
                db.DistrictSummaries.Add(new DistrictSummary
                {
                    NeighborhoodId = id,
                    SummaryText = outcome.SummaryText!,
                    GeneratedAt = now,
                    ScoreSignature = signature,
                });
            }
            else
            {
                existing.SummaryText = outcome.SummaryText!;
                existing.GeneratedAt = now;
                existing.ScoreSignature = signature;
            }

            // Saved per-district rather than once at the end (unlike e.g. HealthAccessIngestionJob's
            // single bulk save): this job calls an unreliable third-party API across several
            // minutes, so a mid-run failure (Gemini goes down, the process restarts) should keep
            // whatever districts already succeeded rather than lose the whole run's progress.
            await db.SaveChangesAsync(ct);
        }
    }
}

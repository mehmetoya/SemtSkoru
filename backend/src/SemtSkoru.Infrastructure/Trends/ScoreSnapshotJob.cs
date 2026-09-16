using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Localization;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Trends;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Trends;

/// <summary>
/// Weekly job with two responsibilities that share one run because the second one is only ever
/// possible using data the first one wrote (see this job's Hangfire registration in Program.cs
/// for the cadence/staggering reasoning):
///
///  1. SNAPSHOTTING - writes one ScoreSnapshot row per (district, dimension) for whatever
///     dimensions the district actually has real data for right now, plus one more row for the
///     district's overall score (Dimension == null). See ScoreSnapshot's remarks for why this
///     table exists at all: before this job's first-ever run, this codebase never persisted a
///     score anywhere - INeighborhoodScoringService always recomputes fresh, on demand, from
///     current readings. This is the ONLY place that history gets written, ever, and it is never
///     backfilled - the day this job first runs is the day this feature's history starts. This
///     half is locale-agnostic (a raw score has no language).
///
///  2. TREND GENERATION - for each district, looks for the single most recent snapshot recorded
///     at least MinimumBaselineAge ago and, if DistrictTrendDeltas finds a real, meaningfully
///     sized difference between that baseline and the district's current score, asks Gemini (via
///     DistrictTrendService, which reuses the same IDistrictAssistantAiClient/GeminiClient the AI
///     Semt Asistanı and DistrictSummaryService already share) for a short grounded description
///     of the change, IN EACH SUPPORTED LOCALE (see AiLocale.All - "tr" and "en" today). A
///     district with no eligible baseline yet is skipped entirely for BOTH locales - no Gemini
///     call, no pacing delay spent - which is true for EVERY district for at least
///     MinimumBaselineAge after this feature first deploys: this is the expected cold-start
///     state, not a bug. See DistrictTrendService's remarks for how it degrades honestly, and
///     DistrictTrendBadge.tsx on the frontend for how the UI shows nothing at all in that state.
///
/// Like DistrictSummaryGenerationJob, this job paces its own outbound Gemini calls (ASP.NET
/// Core's inbound rate limiter has no idea this job exists) and skips (district, locale) pairs
/// whose comparison basis hasn't changed since that SPECIFIC locale's last run
/// (DistrictTrendSignature, keyed per locale - see GenerateTrendsAsync), so a run's real Gemini
/// spend is usually far below the worst-case figure the cadence in Program.cs is sized against.
/// </summary>
public sealed class ScoreSnapshotJob(
    INeighborhoodDirectory directory,
    INeighborhoodScoringService scoringService,
    IDistrictTrendService trendService,
    AppDbContext db,
    ILogger<ScoreSnapshotJob> logger,
    TimeProvider timeProvider)
{
    // A baseline must be at least this old before a comparison against it is trusted enough to
    // narrate - not because a fresher snapshot's numbers would be any less real, but because this
    // job's own cadence (Cron.Weekly(), see Program.cs) IS the snapshot interval: a snapshot
    // younger than one full cycle could only exist because of a redeploy or a manually-triggered
    // re-run, not a real week of possible score movement having elapsed. One full cadence is also
    // the minimum span over which this app's slowest-moving weekly-ingested dimensions (green
    // space, health access, transit access - see their own jobs' cadence comments) could even
    // have a new İBB publish land at all; anything shorter risks narrating same-run re-ingestion
    // jitter (e.g. parking occupancy sampled a few hours apart) as if it were a real trend.
    private static readonly TimeSpan MinimumBaselineAge = TimeSpan.FromDays(7);

    // Same reasoning as DistrictSummaryGenerationJob.ThrottleDelay/RateLimitBackoff: one call
    // every 5 seconds keeps this job comfortably under the free tier's lowest publicly-reported
    // per-minute figure even before accounting for the interactive AI Semt Asistanı's own share
    // of the same budget, and a 30s backoff avoids hammering Gemini with the remaining
    // (district, locale) pairs (up to 77 more, worst case) immediately after it's already said to
    // slow down.
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RateLimitBackoff = TimeSpan.FromSeconds(30);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var names = await directory.GetAllNamesAsync(ct);
        var scores = await scoringService.GetAllScoresAsync(ct);

        await TakeSnapshotsAsync(names.Keys, scores, now, ct);
        await GenerateTrendsAsync(names, scores, now, ct);
    }

    private async Task TakeSnapshotsAsync(
        IEnumerable<string> ids,
        IReadOnlyDictionary<string, NeighborhoodScoreResult> scores,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();

            if (!scores.TryGetValue(id, out var score) || !score.HasAnyData)
            {
                continue; // nothing ingested for this district yet - nothing real to snapshot.
            }

            AddRowIfPresent(id, "airQuality", score.AirQuality, now);
            AddRowIfPresent(id, "greenSpace", score.GreenSpace, now);
            AddRowIfPresent(id, "transportation", score.Transportation, now);
            AddRowIfPresent(id, "parking", score.Parking, now);
            AddRowIfPresent(id, "healthAccess", score.HealthAccess, now);
            AddRowIfPresent(id, "transitAccess", score.TransitAccess, now);

            if (score.Overall is { } overall)
            {
                db.ScoreSnapshots.Add(new ScoreSnapshot
                {
                    NeighborhoodId = id,
                    Dimension = null,
                    Score = overall.Value,
                    RecordedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private void AddRowIfPresent(string neighborhoodId, string dimension, DimensionScore dimensionScore, DateTimeOffset now)
    {
        if (dimensionScore.Value is not { } value)
        {
            return; // "Veri yok" for this dimension right now - nothing real to snapshot.
        }

        db.ScoreSnapshots.Add(new ScoreSnapshot
        {
            NeighborhoodId = neighborhoodId,
            Dimension = dimension,
            Score = value.Value,
            RecordedAt = now,
        });
    }

    private async Task GenerateTrendsAsync(
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, NeighborhoodScoreResult> scores,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var cutoff = now - MinimumBaselineAge;

        // Keyed by (NeighborhoodId, Locale), matching DistrictTrendSummaryConfiguration's
        // composite primary key - so the "unchanged since last generation" skip below (which
        // compares ComparisonSignature) is evaluated independently per locale: a district whose
        // "tr" row is already up to date but has no "en" row yet must still get an "en" Gemini
        // call.
        var existingTrends = await db.DistrictTrendSummaries.ToDictionaryAsync(t => (t.NeighborhoodId, t.Locale), ct);
        var isFirstCall = true;

        foreach (var (id, name) in names.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            if (!scores.TryGetValue(id, out var score) || !score.HasAnyData)
            {
                continue;
            }

            // The baseline lookup and delta computation are locale-agnostic (they compare raw
            // scores, never AI prose), so both are done once per district, not once per locale.
            var baseline = await GetBaselineAsync(id, cutoff, ct);
            if (baseline is null)
            {
                // Cold start, or this district's oldest snapshot isn't old enough yet - no
                // Gemini call, no pacing delay spent (mirrors DistrictSummaryGenerationJob's own
                // "neither branch spends a Gemini call" skip for a district with no data at all).
                // Applies to BOTH locales equally - there is nothing to narrate in any language.
                await RemoveStaleTrendsIfAnyAsync(id, existingTrends, ct);
                continue;
            }

            var deltas = DistrictTrendDeltas.Compute(baseline, score);
            if (deltas.Count == 0)
            {
                // A real baseline exists but nothing moved enough to be worth narrating. If a
                // PREVIOUS run's trend text is still sitting there in either locale (comparing
                // against an even older baseline whose delta WAS meaningful at the time), it no
                // longer reflects this district's current standing and must not keep being shown
                // - see DistrictTrendBadge's "renders nothing" contract on the frontend.
                await RemoveStaleTrendsIfAnyAsync(id, existingTrends, ct);
                continue;
            }

            var signature = DistrictTrendSignature.Compute(deltas);

            foreach (var locale in AiLocale.All)
            {
                ct.ThrowIfCancellationRequested();

                var existing = existingTrends.GetValueOrDefault((id, locale));
                if (existing is not null && existing.ComparisonSignature == signature)
                {
                    logger.LogInformation(
                        "Skipping {Locale} district trend for {NeighborhoodId}: comparison unchanged since {GeneratedAt}",
                        locale, id, existing.GeneratedAt);
                    continue; // same meaningful deltas as last time for THIS locale - don't spend a Gemini call.
                }

                if (!isFirstCall)
                {
                    await Task.Delay(ThrottleDelay, ct);
                }
                isFirstCall = false;

                var outcome = await trendService.GenerateTrendAsync(name, deltas, locale, ct);

                if (outcome.Kind == DistrictTrendOutcomeKind.NotConfigured)
                {
                    logger.LogInformation("Gemini API key not configured; skipping district trend generation entirely");
                    return; // no key in this environment - every remaining (district, locale) pair would fail identically.
                }

                if (outcome.Kind == DistrictTrendOutcomeKind.RateLimited)
                {
                    logger.LogWarning(
                        "Gemini rate-limited district trend generation at {NeighborhoodId}/{Locale}; backing off", id, locale);
                    await Task.Delay(RateLimitBackoff, ct);
                    continue;
                }

                if (outcome.Kind != DistrictTrendOutcomeKind.Ok)
                {
                    logger.LogInformation(
                        "No usable district trend generated for {NeighborhoodId}/{Locale}: {Outcome}", id, locale, outcome.Kind);
                    continue;
                }

                if (existing is null)
                {
                    db.DistrictTrendSummaries.Add(new DistrictTrendSummary
                    {
                        NeighborhoodId = id,
                        Locale = locale,
                        SummaryText = outcome.SummaryText!,
                        GeneratedAt = now,
                        ComparisonSignature = signature,
                    });
                }
                else
                {
                    existing.SummaryText = outcome.SummaryText!;
                    existing.GeneratedAt = now;
                    existing.ComparisonSignature = signature;
                }

                // Saved per (district, locale) rather than once at the end (unlike e.g.
                // HealthAccessIngestionJob's single bulk save): this job calls an unreliable
                // third-party API across several minutes, so a mid-run failure should keep
                // whatever (district, locale) pairs already succeeded rather than lose the whole
                // run's progress - mirrors DistrictSummaryGenerationJob.
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task RemoveStaleTrendsIfAnyAsync(
        string neighborhoodId,
        IReadOnlyDictionary<(string NeighborhoodId, string Locale), DistrictTrendSummary> existingTrends,
        CancellationToken ct)
    {
        var removedAny = false;
        foreach (var locale in AiLocale.All)
        {
            if (existingTrends.TryGetValue((neighborhoodId, locale), out var existing))
            {
                db.DistrictTrendSummaries.Remove(existing);
                removedAny = true;
            }
        }

        if (removedAny)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    // Finds the single most recent snapshot moment (RecordedAt) at least MinimumBaselineAge old
    // for this district, then returns every dimension row recorded at exactly that moment as one
    // coherent baseline - deliberately not the latest-per-dimension independently, which could
    // mix values from different weeks and compare today's score against a patchwork that was
    // never real at any single point in time.
    private async Task<ScoreSnapshotBaseline?> GetBaselineAsync(string neighborhoodId, DateTimeOffset cutoff, CancellationToken ct)
    {
        var latestEligible = await db.ScoreSnapshots
            .Where(s => s.NeighborhoodId == neighborhoodId && s.RecordedAt <= cutoff)
            .OrderByDescending(s => s.RecordedAt)
            .Select(s => (DateTimeOffset?)s.RecordedAt)
            .FirstOrDefaultAsync(ct);

        if (latestEligible is not { } recordedAt)
        {
            return null; // no snapshot old enough exists yet for this district - cold start.
        }

        var rows = await db.ScoreSnapshots
            .Where(s => s.NeighborhoodId == neighborhoodId && s.RecordedAt == recordedAt)
            .ToListAsync(ct);

        int? Value(string dimension) => rows.FirstOrDefault(r => r.Dimension == dimension)?.Score;

        return new ScoreSnapshotBaseline(
            recordedAt,
            Value("airQuality"),
            Value("greenSpace"),
            Value("transportation"),
            Value("parking"),
            Value("healthAccess"),
            Value("transitAccess"),
            rows.FirstOrDefault(r => r.Dimension == null)?.Score);
    }
}

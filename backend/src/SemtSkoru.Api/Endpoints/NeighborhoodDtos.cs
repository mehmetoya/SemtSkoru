using NetTopologySuite.Geometries;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Search;
using SemtSkoru.Domain;

namespace SemtSkoru.Api.Endpoints;

public sealed record NeighborhoodSummaryDto(string Id, string Name, Geometry Boundary, int? OverallScore);

// Deliberately just id+name: callers that only need a district's display name (e.g. the
// downloadable score-card image routes) shouldn't have to pay for a full boundary fetch plus
// a 6-dimension scoring pass across all 39 districts just to resolve one label.
public sealed record NeighborhoodNameDto(string Id, string Name);

public sealed record DimensionScoreDto(int? Score, string? Freshness, string? SourceName, DateTimeOffset? PublishedAt)
{
    public static DimensionScoreDto From(DimensionScore dimension) =>
        new(dimension.Value?.Value, dimension.Freshness?.ToString(), dimension.SourceName, dimension.PublishedAt);
}

public sealed record NeighborhoodComparisonDto(NeighborhoodScoreDto A, NeighborhoodScoreDto B);

// Null whenever no usable AI comparison summary is available for this pair right now - no Gemini
// key configured, the live generation call failed/timed out/was rate-limited, the model's
// response wasn't usable, or neither district shares a scored dimension with the other (see
// ComparisonSummaryOrchestrator/ComparisonSummaryService) - never a fabricated placeholder. Same
// honest-absence contract as DistrictSummaryDto above; the frontend renders nothing for a null
// summary here exactly like DistrictSummaryBadge does.
public sealed record ComparisonSummaryDto(string Text, DateTimeOffset GeneratedAt)
{
    public static ComparisonSummaryDto From(ComparisonSummary summary) =>
        new(summary.SummaryText, summary.GeneratedAt);
}

// GET /api/neighborhoods/compare/summary's whole response body - deliberately just this one
// nullable field (no status/message discriminator like AssistantResponseDto has) because, unlike
// the Assistant's interactive form, this is a passive auto-triggered fetch the frontend reacts to
// the exact same way regardless of WHY a summary isn't available - see ComparisonSummaryBadge.tsx.
public sealed record ComparisonSummaryResponseDto(ComparisonSummaryDto? Summary);

// Null whenever no cached AI summary exists yet for this district - Gemini not configured, the
// weekly DistrictSummaryGenerationJob hasn't run for it yet, or every generation attempt so far
// came back unusable (see DistrictSummaryOutcome) - never a fabricated placeholder.
public sealed record DistrictSummaryDto(string Text, DateTimeOffset GeneratedAt);

// Null whenever no cached trend summary exists yet for this district - covers every honest
// reason from DistrictTrendOutcome (Gemini not configured, no usable text) PLUS the two reasons
// unique to trends: no baseline snapshot old enough exists yet (cold start - see
// ScoreSnapshotJob, true for every district for at least a week after this feature first
// deploys) or nothing about the district's score changed enough since its baseline to be worth
// narrating. Never a fabricated placeholder or a "check back later" filler - see
// web/components/DistrictTrendBadge.tsx, which renders nothing at all when this is null.
public sealed record DistrictTrendDto(string Text, DateTimeOffset GeneratedAt);

// GET /api/neighborhoods/search's whole response body. Deliberately just ids - never names,
// scores, or AI-authored reasoning - because the frontend already holds every district's full
// summary in memory from its own earlier server-side fetch (see web/app/[locale]/page.tsx) and
// only needs to know which real ids matched and in what order. `Dimensions` echoes back the
// validated (never hallucinated) subset of the 6 real dimension keys the query was judged to
// concern, purely for frontend transparency (e.g. showing "Filtering by: ..." chips) - mirrors
// AssistantResponseDto's Status/Message shape: Status is a stable machine-readable discriminator
// for the frontend (matches DistrictSearchOutcomeKind), Message is the Turkish, backend-authored
// sentence for non-Ok statuses, which the frontend maps to its own translated copy rather than
// rendering directly (see AssistantClient.tsx's own comment on why) - see
// NeighborhoodEndpoints.cs for which HTTP status code each Status comes back with.
public sealed record DistrictSearchResponseDto(
    IReadOnlyList<string> MatchedIds,
    IReadOnlyList<string> Dimensions,
    string Status,
    string? Message,
    // Only meaningful alongside an "Ok" Status: "Model" for the normal path, "KeywordFallback"
    // when Gemini was unreachable and the dimensions came from DistrictSearchService's own keyword
    // table instead (see DistrictSearchMatchSource). Defaulted rather than repeated at each of the
    // non-Ok call sites in NeighborhoodEndpoints.cs, where no matching happened at all.
    string MatchedBy = nameof(DistrictSearchMatchSource.Model))
{
    public static DistrictSearchResponseDto From(DistrictSearchOutcome outcome) => new(
        outcome.Matches.Select(m => m.NeighborhoodId).ToList(),
        outcome.Dimensions,
        nameof(DistrictSearchOutcomeKind.Ok),
        null,
        outcome.Source.ToString());
}

public sealed record NeighborhoodScoreDto(
    string NeighborhoodId,
    DimensionScoreDto AirQuality,
    DimensionScoreDto GreenSpace,
    DimensionScoreDto Transportation,
    DimensionScoreDto Parking,
    DimensionScoreDto HealthAccess,
    DimensionScoreDto TransitAccess,
    int? Overall,
    bool IsComplete,
    DistrictSummaryDto? Summary = null,
    DistrictTrendDto? Trend = null)
{
    // `summary`/`trend` default to null for callers that don't have one handy (e.g. /compare and
    // the AI Semt Asistanı's recommendation cards, which build this same DTO from a
    // NeighborhoodScoreResult that was never joined against DistrictSummaries/
    // DistrictTrendSummaries) - an honestly-absent field there, not a wrong one, since neither of
    // those surfaces renders either today.
    public static NeighborhoodScoreDto From(
        NeighborhoodScoreResult result, DistrictSummaryDto? summary = null, DistrictTrendDto? trend = null) => new(
        result.NeighborhoodId,
        DimensionScoreDto.From(result.AirQuality),
        DimensionScoreDto.From(result.GreenSpace),
        DimensionScoreDto.From(result.Transportation),
        DimensionScoreDto.From(result.Parking),
        DimensionScoreDto.From(result.HealthAccess),
        DimensionScoreDto.From(result.TransitAccess),
        result.Overall?.Value,
        result.IsComplete,
        summary,
        trend);
}

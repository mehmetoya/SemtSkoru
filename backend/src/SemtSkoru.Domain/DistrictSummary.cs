namespace SemtSkoru.Domain;

/// <summary>
/// The AI-generated "standout traits" summary for one district (see
/// SemtSkoru.Application.Summaries.DistrictSummaryService), produced by a scheduled Hangfire job
/// (SemtSkoru.Infrastructure.Summaries.DistrictSummaryGenerationJob) rather than live per page
/// view - see that job's remarks for the shared Gemini-quota reasoning this whole table exists
/// to protect. Its own table, not bolted onto an existing reading table (e.g. a new nullable
/// column on Neighborhood): unlike AirQualityReading/GreenSpaceReading/etc., this isn't a raw
/// ingested reading with its own DataSourceMetadata - it's a derived, regenerable artifact built
/// FROM those readings' already-computed scores, with its own lifecycle (GeneratedAt, and a
/// staleness signature) that has nothing to do with any single upstream İBB data source.
/// </summary>
public sealed class DistrictSummary
{
    public required string NeighborhoodId { get; init; }

    /// <summary>The 1-2 sentence Turkish "standout traits" summary text - see
    /// DistrictSummaryService's SystemInstruction for the exact grounding rules the model must
    /// follow to produce this (never a fact beyond the district's own 6 dimension scores).</summary>
    public required string SummaryText { get; set; }

    public required DateTimeOffset GeneratedAt { get; set; }

    /// <summary>A deterministic, compact encoding of the six dimension scores' VALUES at
    /// generation time (see DistrictSummarySignature) - compared against the district's current
    /// scores on every DistrictSummaryGenerationJob run to decide whether the numbers actually
    /// changed since this summary was written, so an ingestion job re-syncing identical data
    /// never burns a Gemini call regenerating text that would come out the same.</summary>
    public required string ScoreSignature { get; set; }
}

namespace SemtSkoru.Domain;

/// <summary>
/// A single historical data point: one district's score for one dimension (or its overall,
/// equal-weight-average score, when Dimension is null) at one point in time. Written only by
/// SemtSkoru.Infrastructure.Trends.ScoreSnapshotJob (weekly) and never updated or deleted once
/// written for a district whose data is still real - unlike every reading table elsewhere in
/// this app (AirQualityReading, ParkingReading, ...), which upserts a single current-state row
/// per district, this is an append-only log: real history that SemtSkoru.Application.Trends
/// needs to exist at all, and that no ingestion job is allowed to overwrite.
///
/// The whole reason this table exists (see SPEC.md's "never fabricate" rule and
/// SemtSkoru.Application.Trends.DistrictTrendService's remarks): before this feature, this
/// codebase had NEVER persisted a score anywhere - INeighborhoodScoringService always recomputes
/// fresh from current readings, on demand, with nothing kept from the last time it was computed.
/// A trend narrative ("otopark skoru arttı") needs a REAL prior number to compare against; the
/// only alternatives to a table like this are inventing one (forbidden) or claiming a trend that
/// isn't grounded in anything real (also forbidden). This table starts accumulating history from
/// the day this feature is deployed, not before - there is no backfill, and there cannot honestly
/// be one. See ScoreSnapshotJob's remarks for how the feature degrades to showing nothing at all
/// for however long it takes this table to accumulate one meaningfully-old snapshot per district.
/// </summary>
public sealed class ScoreSnapshot
{
    /// <summary>Surrogate key - this table is an append-only log (many rows accumulate per
    /// district over time), unlike the single-row-per-district reading tables elsewhere in this
    /// app, so NeighborhoodId alone can never be a primary key here.</summary>
    public int Id { get; init; }

    public required string NeighborhoodId { get; init; }

    /// <summary>One of the six real dimension keys ("airQuality", "greenSpace",
    /// "transportation", "parking", "healthAccess", "transitAccess" - the same vocabulary
    /// DistrictSummaryService's KnownDimensions set uses), or null for the district's overall
    /// score at that moment.</summary>
    public string? Dimension { get; init; }

    /// <summary>The real 0-100 score value at RecordedAt. A dimension with no data for this
    /// district at snapshot time gets no row at all (see ScoreSnapshotJob) rather than a
    /// fabricated placeholder value - the same "Veri yok" boundary as everywhere else in this
    /// app, just applied to whether a row exists rather than to a nullable column.</summary>
    public required int Score { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }
}

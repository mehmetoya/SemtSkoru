namespace SemtSkoru.Application.Trends;

/// <summary>
/// One district's baseline snapshot - the single most recent ScoreSnapshot old enough (see
/// ScoreSnapshotJob.MinimumBaselineAge) to compare against its current score. Mirrors
/// NeighborhoodScoreResult's six-dimension shape so DistrictTrendDeltas can line the two up
/// field by field; a null dimension here means that specific dimension had no reading (hence no
/// snapshot row - see ScoreSnapshot) at baseline time, not a zero score.
/// </summary>
public sealed record ScoreSnapshotBaseline(
    DateTimeOffset RecordedAt,
    int? AirQuality,
    int? GreenSpace,
    int? Transportation,
    int? Parking,
    int? HealthAccess,
    int? TransitAccess,
    int? Overall);

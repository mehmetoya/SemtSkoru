using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

/// <summary>
/// A single dimension's score, or the explicit absence of one when no raw reading exists yet.
/// SourceName/PublishedAt are carried through so the UI can always show where a score came
/// from and when that data is from, per SPEC.md's "Always" boundary.
/// </summary>
public sealed record DimensionScore(
    Score? Value,
    DataFreshnessStatus? Freshness,
    string? SourceName,
    DateTimeOffset? PublishedAt)
{
    public static readonly DimensionScore NoData = new(null, null, null, null);

    public bool HasData => Value is not null;
}

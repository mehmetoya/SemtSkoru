using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

/// <summary>
/// A single dimension's score, or the explicit absence of one when no raw reading exists yet.
/// </summary>
public sealed record DimensionScore(Score? Value, DataFreshnessStatus? Freshness)
{
    public static readonly DimensionScore NoData = new(null, null);

    public bool HasData => Value is not null;
}

namespace SemtSkoru.Application.Trends;

/// <summary>
/// One dimension's real, already-computed score change between a baseline snapshot and a
/// district's current score - see DistrictTrendDeltas.Compute, the only place these are ever
/// constructed. Both PreviousScore and CurrentScore always come from a real ScoreSnapshot row
/// and a real NeighborhoodScoreResult dimension respectively - never estimated, never
/// interpolated for a gap in either side.
/// </summary>
public sealed record DimensionDelta(string Dimension, int PreviousScore, int CurrentScore)
{
    public int Delta => CurrentScore - PreviousScore;
}

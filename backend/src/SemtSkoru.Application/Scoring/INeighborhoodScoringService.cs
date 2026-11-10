namespace SemtSkoru.Application.Scoring;

public interface INeighborhoodScoringService
{
    /// <summary>Returns null when no neighborhood with this id exists.</summary>
    Task<NeighborhoodScoreResult?> GetScoreAsync(string neighborhoodId, CancellationToken ct);

    /// <summary>Scores every neighborhood in a handful of bulk queries, not one round trip per id.</summary>
    Task<IReadOnlyDictionary<string, NeighborhoodScoreResult>> GetAllScoresAsync(CancellationToken ct);
}

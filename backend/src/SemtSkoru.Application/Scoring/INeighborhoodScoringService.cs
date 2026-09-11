namespace SemtSkoru.Application.Scoring;

public interface INeighborhoodScoringService
{
    /// <summary>Returns null when no neighborhood with this id exists.</summary>
    Task<NeighborhoodScoreResult?> GetScoreAsync(string neighborhoodId, CancellationToken ct);
}

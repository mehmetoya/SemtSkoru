namespace SemtSkoru.Application.Assistant;

/// <summary>
/// Id -> real Turkish display name for every neighborhood. Kept separate from
/// INeighborhoodScoringRepository (which is about raw ingested readings, not identity) so the
/// AI assistant's prompt-building can look up real district names without pulling
/// scoring-specific concerns into this feature, and so it stays trivially fakeable in tests.
/// </summary>
public interface INeighborhoodDirectory
{
    Task<IReadOnlyDictionary<string, string>> GetAllNamesAsync(CancellationToken ct);
}

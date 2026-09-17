namespace SemtSkoru.Application.Search;

public enum DistrictSearchOutcomeKind
{
    /// <summary>The query mapped to at least one real dimension - see Dimensions/Matches. Matches
    /// may still legitimately be empty (a valid dimension was recognized, but no district
    /// currently scores well enough on it to count as a match) - that's a different, more honest
    /// outcome than NoUsableCriteria below and callers should tell them apart.</summary>
    Ok,

    /// <summary>The free-text query was empty/whitespace-only.</summary>
    InvalidRequest,

    /// <summary>No Gemini API key is configured yet.</summary>
    NotConfigured,

    /// <summary>The app's own shared-quota guard or Gemini's own rate limit rejected this call.</summary>
    RateLimited,

    /// <summary>Gemini call failed for any other reason (network, timeout, non-success status).</summary>
    Unavailable,

    /// <summary>
    /// Gemini responded, but nothing usable came out of it: unparseable output, every dimension it
    /// named was hallucinated (not one of the real 6), or the model itself judged the query
    /// unrelated to any of them (an empty "dimensions" array is a legitimate, honest model
    /// response for an off-topic query - see DistrictSearchService's SystemInstruction). Never
    /// surfaced as "show every district" or any other fabricated fallback; always honest emptiness.
    /// </summary>
    NoUsableCriteria,
}

/// <summary>A single real district that scores well on the requested dimension(s), ranked by
/// MatchScore (the equal-weight average of its OWN real score on just the dimensions it actually
/// has data for among those requested - see DistrictSearchService.RankDistricts). Carries only an
/// id - never a name or reasoning - because the frontend already holds every district's full,
/// already-fetched summary in memory (see web/app/[locale]/page.tsx) and only needs to know which
/// ids matched and in what order.</summary>
public sealed record DistrictSearchMatch(string NeighborhoodId, int MatchScore);

public sealed record DistrictSearchOutcome(
    DistrictSearchOutcomeKind Kind,
    IReadOnlyList<string> Dimensions,
    IReadOnlyList<DistrictSearchMatch> Matches)
{
    public static DistrictSearchOutcome Ok(IReadOnlyList<string> dimensions, IReadOnlyList<DistrictSearchMatch> matches) =>
        new(DistrictSearchOutcomeKind.Ok, dimensions, matches);

    public static readonly DistrictSearchOutcome InvalidRequest = new(DistrictSearchOutcomeKind.InvalidRequest, [], []);
    public static readonly DistrictSearchOutcome NotConfigured = new(DistrictSearchOutcomeKind.NotConfigured, [], []);
    public static readonly DistrictSearchOutcome RateLimited = new(DistrictSearchOutcomeKind.RateLimited, [], []);
    public static readonly DistrictSearchOutcome Unavailable = new(DistrictSearchOutcomeKind.Unavailable, [], []);
    public static readonly DistrictSearchOutcome NoUsableCriteria = new(DistrictSearchOutcomeKind.NoUsableCriteria, [], []);
}

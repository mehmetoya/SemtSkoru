using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Assistant;

/// <summary>
/// One AI-recommended district, carrying the SAME real, already-computed NeighborhoodScoreResult
/// the rest of the app shows everywhere else (SPEC.md's "never fabricate a score" boundary
/// applies here exactly as much as anywhere) - Reasoning is the only part of this record that
/// came from the model, and even that is expected to only cite the numbers in Score.
/// </summary>
public sealed record AssistantRecommendation(
    string NeighborhoodId,
    string NeighborhoodName,
    string Reasoning,
    NeighborhoodScoreResult Score);

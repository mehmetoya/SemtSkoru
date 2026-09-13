namespace SemtSkoru.Application.Assistant;

public interface IDistrictAssistantService
{
    Task<AssistantOutcome> GetRecommendationsAsync(string userQuery, CancellationToken ct);
}

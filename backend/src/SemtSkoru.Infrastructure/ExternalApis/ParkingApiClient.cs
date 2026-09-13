using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SemtSkoru.Infrastructure.ExternalApis;

/// <summary>
/// Calls the İBB İSPARK (city-run parking garages) open data web service. Verified live
/// (2026-09-13) and documented in docs/data-sources.md. No API key required.
/// </summary>
public sealed class ParkingApiClient(HttpClient httpClient) : IParkingApiClient
{
    private const string Endpoint = "https://api.ibb.gov.tr/ispark/Park";

    public async Task<IReadOnlyList<ParkingFacilityDto>> GetFacilitiesAsync(CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(Endpoint, ct);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<ApiPark>>(cancellationToken: ct);
        if (items is null)
        {
            return [];
        }

        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.District) && !string.IsNullOrWhiteSpace(i.ParkName))
            .Select(i => new ParkingFacilityDto(i.ParkName, i.District, i.Capacity, i.EmptyCapacity))
            .ToList();
    }

    // Live-verified (2026-09-13) response shape from https://api.ibb.gov.tr/ispark/Park - a
    // JSON array of ~247 objects, field names exactly as below (camelCase, "parkID" with an
    // uppercase ID). "district" is already the uppercase Turkish ilçe name. "isOpen" and
    // "workHours" exist but aren't used here - we didn't verify what "closed" means for a
    // facility's reported capacity, so treating every facility's numbers at face value is the
    // more honest choice than guessing at an undocumented distinction.
    private sealed record ApiPark(
        [property: JsonPropertyName("parkID")] int ParkId,
        [property: JsonPropertyName("parkName")] string ParkName,
        [property: JsonPropertyName("capacity")] int Capacity,
        [property: JsonPropertyName("emptyCapacity")] int EmptyCapacity,
        [property: JsonPropertyName("district")] string District,
        [property: JsonPropertyName("isOpen")] int IsOpen);
}

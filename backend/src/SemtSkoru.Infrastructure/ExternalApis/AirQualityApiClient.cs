using System.Globalization;
using System.Net.Http.Json;

namespace SemtSkoru.Infrastructure.ExternalApis;

/// <summary>
/// Calls the İBB (Istanbul Metropolitan Municipality) air quality open data web service.
/// Verified live and documented in docs/data-sources.md, section 1. No API key required.
/// </summary>
public sealed class AirQualityApiClient(HttpClient httpClient) : IAirQualityApiClient
{
    private const string Endpoint = "https://api.ibb.gov.tr/havakalitesi/OpenDataPortalHandler/GetAQIByStationId";
    private const string DateFormat = "dd.MM.yyyy HH:mm:ss";

    // The API returns timestamps with no offset in Turkey local time (UTC+3, no DST since 2016).
    // .NET would otherwise parse an offset-less timestamp using the EXECUTING MACHINE's local
    // timezone, silently corrupting the value on any server not also set to UTC+3.
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public async Task<AirQualityReadingDto?> GetLatestReadingAsync(string stationId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-6);
        var url = $"{Endpoint}?StationId={stationId}&StartDate={Format(start)}&EndDate={Format(now)}";

        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<ApiItem>>(cancellationToken: ct);
        if (items is null || items.Count == 0)
        {
            return null;
        }

        var latest = items.MaxBy(i => i.ReadTime);
        if (latest is null)
        {
            return null;
        }

        var readTimeUtc = new DateTimeOffset(latest.ReadTime, TurkeyOffset).ToUniversalTime();
        return new AirQualityReadingDto(readTimeUtc, latest.AQI.AQIIndex);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToOffset(TurkeyOffset).ToString(DateFormat, CultureInfo.InvariantCulture);

    private sealed record ApiItem(DateTime ReadTime, ApiAqi AQI);

    private sealed record ApiAqi(double AQIIndex);
}

using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace SemtSkoru.Infrastructure.ExternalApis;

/// <summary>
/// Calls the İBB (Istanbul Metropolitan Municipality) air quality open data web service.
/// Verified live and documented in docs/data-sources.md, section 1. No API key required.
/// </summary>
public sealed partial class AirQualityApiClient(HttpClient httpClient) : IAirQualityApiClient
{
    private const string Endpoint = "https://api.ibb.gov.tr/havakalitesi/OpenDataPortalHandler/GetAQIByStationId";
    private const string StationsEndpoint = "https://api.ibb.gov.tr/havakalitesi/OpenDataPortalHandler/GetAQIStations";
    private const string DateFormat = "dd.MM.yyyy HH:mm:ss";

    // The API returns timestamps with no offset in Turkey local time (UTC+3, no DST since 2016).
    // .NET would otherwise parse an offset-less timestamp using the EXECUTING MACHINE's local
    // timezone, silently corrupting the value on any server not also set to UTC+3.
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public async Task<AirQualityReadingDto?> GetLatestReadingAsync(string stationId, CancellationToken ct)
    {
        // Live-verified (2026-09-13): the API only computes AQI on the hour - a query whose
        // StartDate/EndDate carry the current minute/second (e.g. 14:23:11) gets back readings
        // snapped to that same off-hour offset every hour, and every one of them has a null AQI.
        // Truncating to the hour boundary is what actually lines up with real computed data.
        var now = DateTimeOffset.UtcNow;
        var endOfHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var start = endOfHour.AddHours(-6);
        var url = $"{Endpoint}?StationId={Uri.EscapeDataString(stationId)}" +
            $"&StartDate={Uri.EscapeDataString(Format(start))}&EndDate={Uri.EscapeDataString(Format(endOfHour))}";

        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<ApiItem>>(cancellationToken: ct);
        if (items is null || items.Count == 0)
        {
            return null;
        }

        // Live-verified (2026-09-12): the station can report a ReadTime slot before its AQI has
        // been computed yet - AQI comes back null even though the request itself succeeded (HTTP
        // 200, non-empty list). Taking the raw MaxBy(ReadTime) would then either NRE on a null AQI
        // or discard an earlier reading in the window that actually has one - skip nulls instead.
        var latest = items.Where(i => i.AQI is not null).MaxBy(i => i.ReadTime);
        if (latest is null)
        {
            return null;
        }

        var readTimeUtc = new DateTimeOffset(latest.ReadTime, TurkeyOffset).ToUniversalTime();
        return new AirQualityReadingDto(readTimeUtc, latest.AQI!.AQIIndex);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToOffset(TurkeyOffset).ToString(DateFormat, CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<AirQualityStationDto>> GetStationsAsync(CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(StationsEndpoint, ct);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<ApiStation>>(cancellationToken: ct);
        if (items is null)
        {
            return [];
        }

        var stations = new List<AirQualityStationDto>();
        foreach (var item in items)
        {
            // Live-verified (2026-09-12): "Location" is always "POINT (lon lat)" - matches
            // GeoJSON's lon-then-lat convention, not lat-then-lon.
            var match = PointRegex().Match(item.Location);
            if (!match.Success)
            {
                continue;
            }

            var lon = double.Parse(match.Groups["lon"].Value, CultureInfo.InvariantCulture);
            var lat = double.Parse(match.Groups["lat"].Value, CultureInfo.InvariantCulture);
            stations.Add(new AirQualityStationDto(item.Id, item.Name, lon, lat));
        }

        return stations;
    }

    [GeneratedRegex(@"POINT \((?<lon>-?[\d.]+) (?<lat>-?[\d.]+)\)")]
    private static partial Regex PointRegex();

    private sealed record ApiItem(DateTime ReadTime, ApiAqi? AQI);

    private sealed record ApiAqi(double AQIIndex);

    private sealed record ApiStation(string Id, string Name, string Location);
}

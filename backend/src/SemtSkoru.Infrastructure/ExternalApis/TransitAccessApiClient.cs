using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace SemtSkoru.Infrastructure.ExternalApis;

/// <summary>
/// Fetches İBB's İETT "Otobüs Durakları Verisi" GeoJSON (city-wide, 15,486 Point features,
/// live-verified 2026-09-13 - see docs/data-sources.md) and extracts each stop's coordinates.
/// Unlike GreenSpaceApiClient/HealthAccessApiClient this dataset's own district field (ILCEID)
/// is never read: it's a numeric code with no reliable public mapping back to a district name
/// (live-verified), so TransitAccessIngestionJob matches stops to districts by point-in-polygon
/// test against each district's real boundary instead, the same technique air quality/traffic
/// already use for their own point sources.
/// </summary>
public sealed class TransitAccessApiClient(HttpClient httpClient) : ITransitAccessApiClient
{
    private const string Endpoint =
        "https://data.ibb.gov.tr/dataset/af3c70e8-82d6-44e2-84cf-2e364c242227/resource/4f28ec8d-7c2d-477b-873d-d17ce5b5e3be/download/iett-otobus-duraklar..geojson";

    public async Task<IReadOnlyList<TransitStopDto>> GetStopsAsync(CancellationToken ct)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());

        await using var stream = await httpClient.GetStreamAsync(Endpoint, ct);
        var featureCollection = await JsonSerializer.DeserializeAsync<FeatureCollection>(stream, options, ct);

        var stops = new List<TransitStopDto>();
        if (featureCollection is null)
        {
            return stops;
        }

        foreach (var feature in featureCollection)
        {
            // Live-verified (2026-09-13): every one of the dataset's 15,486 features is a real
            // Point geometry - defensively skip anything else rather than assume it always will be.
            if (feature.Geometry is not Point point)
            {
                continue;
            }

            stops.Add(new TransitStopDto(point.X, point.Y));
        }

        return stops;
    }
}

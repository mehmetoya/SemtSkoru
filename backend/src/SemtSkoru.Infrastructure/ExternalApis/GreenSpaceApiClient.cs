using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.IO.Converters;

namespace SemtSkoru.Infrastructure.ExternalApis;

/// <summary>
/// Fetches the İBB "İstanbul Kentsel Açık ve Yeşil Alan Koordinatları" GeoJSON dataset
/// (city-wide, ~53 MB, verified in docs/data-sources.md section 2) and extracts parks.
/// </summary>
public sealed class GreenSpaceApiClient(HttpClient httpClient) : IGreenSpaceApiClient
{
    private const string Endpoint =
        "https://data.ibb.gov.tr/dataset/82e809cf-9465-407a-91cd-ac745d6fbc95/resource/41ddb7a6-6931-4176-9614-2c2892da5307/download/yaysis_mahal_geo_data.geojson";

    private const string TypeProperty = "TUR";
    private const string NameProperty = "MAHALLE";
    private const string DistrictProperty = "ILCE";
    private const string ParkType = "Park";

    public async Task<IReadOnlyList<ParkFeatureDto>> GetParksAsync(CancellationToken ct)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());

        await using var stream = await httpClient.GetStreamAsync(Endpoint, ct);
        var featureCollection = await JsonSerializer.DeserializeAsync<FeatureCollection>(stream, options, ct);

        var parks = new List<ParkFeatureDto>();
        if (featureCollection is null)
        {
            return parks;
        }

        foreach (var feature in featureCollection)
        {
            if (feature.Attributes?[TypeProperty] as string != ParkType)
            {
                continue;
            }

            var name = feature.Attributes[NameProperty] as string ?? "";
            var district = feature.Attributes[DistrictProperty] as string ?? "";
            var centroid = feature.Geometry.Centroid.Coordinate;

            parks.Add(new ParkFeatureDto(name, district, centroid));
        }

        return parks;
    }
}

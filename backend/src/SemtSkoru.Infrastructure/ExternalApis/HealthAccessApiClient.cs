using System.Globalization;
using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.IO.Converters;

namespace SemtSkoru.Infrastructure.ExternalApis;

/// <summary>
/// Fetches İBB's "34 Dakika İstanbul Sağlık İndeksi" GeoJSON (city-wide, 901 mahalle-level
/// Polygon features, verified in docs/data-sources.md) and extracts each mahalle's district,
/// name, raw health-access index and population. Unlike GreenSpaceApiClient this dataset's
/// geometry itself is never used - the mahalle's district tag and attributes are enough,
/// since scoring happens at the district level via a population-weighted average
/// (see HealthAccessIngestionJob), not via nearest-feature distance.
/// </summary>
public sealed class HealthAccessApiClient(HttpClient httpClient) : IHealthAccessApiClient
{
    private const string Endpoint =
        "https://data.ibb.gov.tr/dataset/d68cd520-971c-46c1-98cb-6cb66c940604/resource/d90e11be-d5b3-4df2-ba1f-e4356d336ad7/download/saglik_index.geojson";

    private const string DistrictProperty = "ILCE_ADI";
    private const string MahalleProperty = "MAHALLE_ADI";
    private const string PopulationProperty = "KISI_SAYISI";
    private const string HealthIndexProperty = "SAGLIK_INDEX";

    public async Task<IReadOnlyList<HealthMahalleFeatureDto>> GetMahallesAsync(CancellationToken ct)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());

        await using var stream = await httpClient.GetStreamAsync(Endpoint, ct);
        var featureCollection = await JsonSerializer.DeserializeAsync<FeatureCollection>(stream, options, ct);

        var mahalles = new List<HealthMahalleFeatureDto>();
        if (featureCollection is null)
        {
            return mahalles;
        }

        foreach (var feature in featureCollection)
        {
            var district = feature.Attributes?[DistrictProperty] as string;
            var healthIndex = ToDouble(feature.Attributes?[HealthIndexProperty]);
            if (string.IsNullOrWhiteSpace(district) || healthIndex is null)
            {
                continue;
            }

            var mahalleName = feature.Attributes?[MahalleProperty] as string ?? "";
            // Live-verified (2026-09-13): 97 of the 901 mahalles carry KISI_SAYISI as 0 (never
            // observed as a JSON null, but both are treated identically here) - uninhabited
            // industrial/forest zones. ToInt below returns 0 for either case, and
            // HealthAccessIngestionJob excludes zero-population mahalles from its average.
            var population = ToInt(feature.Attributes?[PopulationProperty]);

            mahalles.Add(new HealthMahalleFeatureDto(district, mahalleName, healthIndex.Value, population));
        }

        return mahalles;
    }

    // Live-verified (2026-09-13): System.Text.Json (via GeoJsonConverterFactory's
    // AttributesTable) deserializes GeoJSON numeric properties as boxed System.Decimal, not
    // double/int - converting explicitly here rather than an unchecked (double)/(int) cast,
    // which would throw an InvalidCastException on a boxed decimal.
    private static double? ToDouble(object? value) => value switch
    {
        null => null,
        IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static int ToInt(object? value) => value switch
    {
        null => 0,
        IConvertible convertible => convertible.ToInt32(CultureInfo.InvariantCulture),
        _ => 0,
    };
}

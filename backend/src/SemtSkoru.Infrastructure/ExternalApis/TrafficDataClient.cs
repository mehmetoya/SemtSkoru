using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;
using CsvHelper.Configuration.Attributes;

namespace SemtSkoru.Infrastructure.ExternalApis;

/// <summary>
/// Streams the İBB "Hourly Traffic Density Data Set" CSV for the January 2025 snapshot —
/// the last month published before the source stopped updating (docs/data-sources.md, section 3).
/// ~140 MB / ~1.76M rows for one month, so rows are streamed rather than buffered in memory.
/// </summary>
public sealed class TrafficDataClient(HttpClient httpClient) : ITrafficDataClient
{
    private const string Endpoint =
        "https://data.ibb.gov.tr/dataset/3ee6d744-5da2-40c8-9cd6-0e3e41f1928f/resource/57cb067b-1a0b-460b-8342-7884bd4537e8/download/traffic_density_202501.csv";

    public async IAsyncEnumerable<TrafficRowDto> GetTrafficRowsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await httpClient.GetStreamAsync(Endpoint, ct);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        await foreach (var row in csv.GetRecordsAsync<CsvRow>(ct))
        {
            yield return new TrafficRowDto(row.Latitude, row.Longitude, row.AverageSpeed);
        }
    }

    private sealed class CsvRow
    {
        [Name("LATITUDE")]
        public double Latitude { get; set; }

        [Name("LONGITUDE")]
        public double Longitude { get; set; }

        [Name("AVERAGE_SPEED")]
        public double AverageSpeed { get; set; }
    }
}

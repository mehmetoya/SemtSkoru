using NetTopologySuite.Geometries;

namespace SemtSkoru.Infrastructure.ExternalApis;

public sealed record ParkFeatureDto(string Name, string District, Coordinate Centroid);

public interface IGreenSpaceApiClient
{
    /// <summary>
    /// Returns every "Park"-typed feature (TUR == "Park") from the İBB urban green space
    /// dataset, city-wide, with each feature's geometric centroid.
    /// </summary>
    Task<IReadOnlyList<ParkFeatureDto>> GetParksAsync(CancellationToken ct);
}

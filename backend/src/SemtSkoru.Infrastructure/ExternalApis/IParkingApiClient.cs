namespace SemtSkoru.Infrastructure.ExternalApis;

public sealed record ParkingFacilityDto(string Name, string District, int Capacity, int EmptyCapacity);

public interface IParkingApiClient
{
    /// <summary>
    /// Returns every İSPARK (İBB) parking facility, city-wide, with its real capacity and
    /// currently-empty capacity. Live-verified (2026-09-13): ~247 facilities total, each
    /// already tagged with its own uppercase Turkish district name — no coordinate/point-in-
    /// polygon matching needed, unlike air quality or traffic.
    /// </summary>
    Task<IReadOnlyList<ParkingFacilityDto>> GetFacilitiesAsync(CancellationToken ct);
}

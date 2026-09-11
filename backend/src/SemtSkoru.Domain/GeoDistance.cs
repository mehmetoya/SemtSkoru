using NetTopologySuite.Geometries;

namespace SemtSkoru.Domain;

public static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_000;

    /// <summary>
    /// Great-circle distance between two WGS84 coordinates (Coordinate.X = longitude, Y = latitude),
    /// in meters. Accurate enough for city-scale distances; not a substitute for a proper geodesic
    /// library over long distances.
    /// </summary>
    public static double HaversineMeters(Coordinate a, Coordinate b)
    {
        var lat1 = DegreesToRadians(a.Y);
        var lat2 = DegreesToRadians(b.Y);
        var deltaLat = DegreesToRadians(b.Y - a.Y);
        var deltaLon = DegreesToRadians(b.X - a.X);

        var h = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}

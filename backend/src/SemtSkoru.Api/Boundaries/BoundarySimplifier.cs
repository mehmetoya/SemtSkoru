using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
using NetTopologySuite.Simplify;

namespace SemtSkoru.Api.Boundaries;

/// <summary>
/// Produces the display-only copy of a district boundary that GET /api/neighborhoods ships:
/// topology-preserving simplified, then rounded to 5 decimal places. Pure and deterministic -
/// never touches the stored geometry (see remarks).
/// </summary>
/// <remarks>
/// Why this exists: the seeded OSM/Nominatim boundaries (migrations 20260911173641 and
/// 20260912202126) carry ~39,400 vertices at 7 decimal places, with neighbouring vertices often
/// ~10 cm apart - 940 KB of JSON (~300 KB gzipped) for one list response. Every frontend
/// consumer renders them far coarser than that:
/// <list type="bullet">
/// <item>web/components/NeighborhoodMap.tsx (the compare page's MapLibre choropleth) is the most
/// detailed one. Its fitBounds is capped at <c>maxZoom: 13</c>, which at İstanbul's latitude
/// (~41°N) is ~14.4 m per CSS pixel (~7.2 m per device pixel on a 2x screen). MapLibre's own
/// GeoJSON tiler (geojson-vt) already re-simplifies every source to ~0.375 px per zoom level
/// before drawing, i.e. ~5.4 m at z13 - so detail finer than that never reached the screen at
/// the map's auto-fitted zoom anyway.</item>
/// <item>web/components/DistrictShapeIcon.tsx (36-48 CSS px icons: home page list and highlight
/// cards, the compare page's district pickers, and the score card inside the district page's
/// carousel) re-simplifies client-side to 0.3% of
/// a district's extent - hundreds of metres for most districts - so a few metres here is
/// invisible there.</item>
/// <item>The score-card image routes (web/app/ilce/[id]/kart, web/app/karsilastir/kart) don't
/// use boundaries at all - they call GET /api/neighborhoods/names.</item>
/// </list>
/// Hence <see cref="Tolerance"/> = 0.00005° (~5.5 m north-south, ~4.2 m east-west here): at
/// most ~0.4 CSS px (~0.8 device px on a 2x screen) of deviation at z13, matching what
/// MapLibre was already drawing there, while still holding up when a visitor scroll-zooms a level
/// or two past the auto-fit. Measured against the real seeded data (see
/// BoundarySimplifierTests): ~39,400 -&gt; ~20,700 vertices, 940 KB -&gt; ~416 KB raw,
/// ~304 KB -&gt; ~123 KB gzipped. Rounding to 5 decimals (~1.1 m / ~0.8 m) costs nothing
/// visible either and is what shrinks every remaining coordinate's JSON text.
///
/// <see cref="TopologyPreservingSimplifier"/> (not plain Douglas-Peucker): it never
/// collapses a ring, never drops an island out of a MultiPolygon (Adalar, Şile) and never makes a
/// ring self-intersect, so each district stays a valid geometry of the same type. It works per
/// geometry, so a border shared by two neighbours is simplified independently on each side and
/// the two copies can drift apart by up to the tolerance - a sub-pixel sliver at z13, below the
/// 2 px outline MapLibre draws over it. A coverage-aware simplifier (NTS CoverageSimplifier)
/// would keep shared edges identical, but the seeded boundaries aren't a clean coverage to begin
/// with: they already overlap by ~0.45 km² in total (10 of the 39 fail CoverageValidator -
/// exactly Kadıköy, Beşiktaş and Üsküdar, seeded from a separate, earlier OSM fetch, plus their
/// neighbours), so it buys little here and measured ~50% larger output at the same tolerance.
///
/// Rounding happens AFTER simplification and pointwise (each coordinate on its own, no
/// snap-rounding overlay): GeometryPrecisionReducer's default overlay-based mode was measured to
/// turn Küçükçekmece's Polygon into a 12-part MultiPolygon, which would change the JSON shape the
/// frontend switches on. Pointwise rounding can make a ring touch itself wherever the source has
/// a pinch narrower than the rounding grid - Küçükçekmece really does, under a metre wide near
/// (28.77272, 40.98172), so 5 decimals makes it self-intersect. So the result is always checked
/// (validity, geometry type, part count) and steps down a ladder: 5 decimals, then
/// <see cref="FallbackDecimalPlaces"/> (~0.1 m - Küçükçekmece is valid there and is the only
/// seeded district that needs it), then the unrounded simplified geometry, then the untouched
/// original. The last two never happen with today's data; they exist so a future boundary
/// migration can only ever cost bytes here, never break the map.
///
/// Display-only: the stored <c>Neighborhoods.Boundary</c> column stays full-resolution because
/// ingestion jobs run point-in-polygon tests and area calculations against it
/// (TransitAccessIngestionJob, TrafficIngestionJob, AirQualityIngestionJob,
/// GreenSpaceIngestionJob) - a 5 m error there could move a bus stop or sensor into the wrong
/// district. NTS's simplifiers return new geometries and never mutate their input.
/// </remarks>
public static class BoundarySimplifier
{
    /// <summary>Douglas-Peucker distance tolerance, in degrees (the data is EPSG:4326).</summary>
    public const double Tolerance = 0.00005;

    /// <summary>Decimal places kept in each output coordinate (~1.1 m north-south).</summary>
    public const int DecimalPlaces = 5;

    /// <summary>
    /// Used only when rounding to <see cref="DecimalPlaces"/> would make a geometry invalid.
    /// </summary>
    public const int FallbackDecimalPlaces = 6;

    private static readonly PrecisionModel[] RoundingModels =
    [
        new(Math.Pow(10, DecimalPlaces)),
        new(Math.Pow(10, FallbackDecimalPlaces)),
    ];

    public static Geometry Simplify(Geometry boundary)
    {
        var simplified = TopologyPreservingSimplifier.Simplify(boundary, Tolerance);
        if (!IsSafeReplacementFor(simplified, boundary))
        {
            return boundary;
        }

        foreach (var model in RoundingModels)
        {
            var rounded = GeometryPrecisionReducer.ReducePointwise(simplified, model);
            if (IsSafeReplacementFor(rounded, boundary))
            {
                return rounded;
            }
        }

        return simplified;
    }

    private static bool IsSafeReplacementFor(Geometry candidate, Geometry original) =>
        !candidate.IsEmpty
        && candidate.IsValid
        && candidate.OgcGeometryType == original.OgcGeometryType
        && candidate.NumGeometries == original.NumGeometries;
}

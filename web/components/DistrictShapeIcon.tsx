type Ring = [number, number][];

function ringsOf(boundary: GeoJSON.Geometry): Ring[] {
  if (boundary.type === "Polygon") {
    return boundary.coordinates as Ring[];
  }
  if (boundary.type === "MultiPolygon") {
    return (boundary.coordinates as Ring[][]).flat();
  }
  return [];
}

// A tiny, dependency-free preview of a district's real boundary shape - no MapLibre
// instance needed for a 40x40px thumbnail. Projects lng/lat to a 0-100 viewBox using one
// uniform scale (not independent x/y stretching) so the real shape proportions are kept.
export function DistrictShapeIcon({
  boundary,
  className,
}: {
  boundary: GeoJSON.Geometry;
  className?: string;
}) {
  const rings = ringsOf(boundary);
  if (rings.length === 0) {
    return null;
  }

  const points = rings.flat();
  const lons = points.map((p) => p[0]);
  const lats = points.map((p) => p[1]);
  const minLon = Math.min(...lons);
  const maxLon = Math.max(...lons);
  const minLat = Math.min(...lats);
  const maxLat = Math.max(...lats);
  const scale = Math.max(maxLon - minLon, maxLat - minLat) || 1;

  const project = ([lon, lat]: [number, number]) => {
    const x = ((lon - minLon) / scale) * 100;
    const y = 100 - ((lat - minLat) / scale) * 100; // SVG y grows downward, latitude doesn't
    return `${x.toFixed(2)},${y.toFixed(2)}`;
  };

  const path = rings.map((ring) => `M ${ring.map(project).join(" L ")} Z`).join(" ");

  return (
    <svg
      viewBox="0 0 100 100"
      className={className}
      aria-hidden="true"
      preserveAspectRatio="xMidYMid meet"
    >
      <path d={path} fill="currentColor" />
    </svg>
  );
}

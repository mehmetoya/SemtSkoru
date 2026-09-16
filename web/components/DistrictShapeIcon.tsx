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

// A district's real OSM/Nominatim boundary (also used at full precision by the actual map
// in NeighborhoodMap.tsx) can carry several thousand vertices - live-measured on the
// largest one (Şile's coastline), 4,118 points for a ring rendered at 36-48 CSS px here.
// That's several thousand points of precision for a shape a few dozen pixels wide - most
// of them sub-pixel and visually meaningless, but every one of them was still being
// embedded verbatim as literal text in this icon's SVG path (this component runs
// server-side, in NeighborhoodListCard/HighlightCard/DistrictPicker/NeighborhoodScoreCard,
// so that text ships in the actual page HTML for every district shown). Iterative (not
// recursive - a real boundary can have thousands of points, and this runs during SSR)
// Douglas-Peucker simplification, applied post-projection so epsilon is one fixed value in
// this icon's own 0-100 viewBox space regardless of a district's real-world size.
function simplifyRing(points: Ring, epsilon: number): Ring {
  if (points.length < 3) return points;

  const keep = new Uint8Array(points.length);
  keep[0] = 1;
  keep[points.length - 1] = 1;
  const stack: [number, number][] = [[0, points.length - 1]];

  while (stack.length > 0) {
    const [start, end] = stack.pop()!;
    if (end - start < 2) continue;

    const [x1, y1] = points[start];
    const [x2, y2] = points[end];
    const dx = x2 - x1;
    const dy = y2 - y1;
    const lineLenSq = dx * dx + dy * dy;

    let maxDist = 0;
    let index = -1;
    for (let i = start + 1; i < end; i++) {
      const [x, y] = points[i];
      let dist: number;
      if (lineLenSq === 0) {
        dist = Math.hypot(x - x1, y - y1);
      } else {
        // Perpendicular distance from the point to the infinite line through (x1,y1) and
        // (x2,y2) - the standard Douglas-Peucker measure, not clamped to the segment.
        const t = ((x - x1) * dx + (y - y1) * dy) / lineLenSq;
        dist = Math.hypot(x - (x1 + t * dx), y - (y1 + t * dy));
      }
      if (dist > maxDist) {
        maxDist = dist;
        index = i;
      }
    }

    if (maxDist > epsilon && index !== -1) {
      keep[index] = 1;
      stack.push([start, index], [index, end]);
    }
  }

  return points.filter((_, i) => keep[i] === 1);
}

// Sub-pixel even at this icon's largest use (h-12 = 48px) on a 3x-DPI screen: 100 viewBox
// units there is ~144 device px, so 1 unit is ~1.4 device px and this epsilon is well under
// one.
const SIMPLIFY_EPSILON = 0.3;

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

  const project = ([lon, lat]: [number, number]): [number, number] => {
    const x = ((lon - minLon) / scale) * 100;
    const y = 100 - ((lat - minLat) / scale) * 100; // SVG y grows downward, latitude doesn't
    return [x, y];
  };
  const format = ([x, y]: [number, number]) => `${x.toFixed(2)},${y.toFixed(2)}`;

  const path = rings
    .map((ring) => {
      const simplified = simplifyRing(ring.map(project), SIMPLIFY_EPSILON);
      return `M ${simplified.map(format).join(" L ")} Z`;
    })
    .join(" ");

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

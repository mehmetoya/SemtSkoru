"use client";

import { useEffect, useRef } from "react";
import * as maplibregl from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import type { NeighborhoodSummary } from "../lib/types";
import { getScoreBand } from "../lib/score-band";

// MapLibre resolves its Web Worker script from its own bundled module's import.meta.url;
// under Next.js/Turbopack that URL doesn't match /^https?:/, so resolution silently fails
// and the worker ends up trying to import the page's own HTML, erroring immediately. Every
// GeoJSON source then hangs forever (live-verified: isSourceLoaded never reaches true, so
// the choropleth fill/outline layers get added but never actually render). Pointing it at a
// copy of the worker script served as a static asset (see scripts/copy-maplibre-worker.mjs)
// sidesteps the broken auto-detection with a real, always-http(s) URL.
maplibregl.setWorkerUrl("/maplibre-gl-worker.mjs");

// Raw OSM raster tiles, per SPEC.md's "OpenStreetMap tabanlı, ücretsiz harita tile'ları"
// choice. tile.openstreetmap.org's usage policy discourages heavy production traffic
// without caching/a paid tile provider -- fine for this MVP's scale, revisit before growth.
const OSM_STYLE: maplibregl.StyleSpecification = {
  version: 8,
  sources: {
    osm: {
      type: "raster",
      tiles: ["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],
      tileSize: 256,
      attribution: "&copy; OpenStreetMap contributors",
    },
  },
  layers: [{ id: "osm", type: "raster", source: "osm" }],
};

const ISTANBUL_CENTER: [number, number] = [29.02, 41.02];

// Selection A / B colors - kept distinct from the score-band traffic-light palette
// (emerald/amber/red) so map selection state is never confused with a score's status.
const COLOR_A = "#2563eb";
const COLOR_B = "#c026d3";
const COLOR_NO_DATA = "#94a3b8";
// Once both A and B are picked, every other district fades to this neutral gray
// (plus lower opacity below) so the two being compared are what actually stands out.
const COLOR_MUTED = "#cbd5e1";

// Same bands as lib/score-band.ts's SCORE_BAND_STYLES, as raw hex - MapLibre's fill-color
// paint property can't consume Tailwind classes.
const BAND_FILL: Record<string, string> = {
  good: "#10b981",
  moderate: "#f59e0b",
  poor: "#ef4444",
  unknown: COLOR_NO_DATA,
};

function colorFor(
  id: string,
  overallScore: number | null,
  selectedIds: (string | undefined)[],
): string {
  const [a, b] = selectedIds;
  if (a === id) return COLOR_A;
  if (b === id) return COLOR_B;
  if (a && b) return COLOR_MUTED;
  return BAND_FILL[getScoreBand(overallScore)];
}

export function NeighborhoodMap({
  neighborhoods,
  selectedIds = [],
  onSelectDistrict,
  className = "h-96 w-full",
}: {
  neighborhoods: NeighborhoodSummary[];
  selectedIds?: (string | undefined)[];
  onSelectDistrict?: (id: string) => void;
  className?: string;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<maplibregl.Map | null>(null);
  const onSelectRef = useRef(onSelectDistrict);
  useEffect(() => {
    onSelectRef.current = onSelectDistrict;
  });

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;

    const map = new maplibregl.Map({
      container: containerRef.current,
      style: OSM_STYLE,
      center: ISTANBUL_CENTER,
      zoom: 10,
    });
    mapRef.current = map;

    map.on("click", "district-fill", (e) => {
      const id = e.features?.[0]?.properties?.id;
      if (typeof id === "string") onSelectRef.current?.(id);
    });
    map.on("mouseenter", "district-fill", () => {
      map.getCanvas().style.cursor = onSelectRef.current ? "pointer" : "";
    });
    map.on("mouseleave", "district-fill", () => {
      map.getCanvas().style.cursor = "";
    });

    // MapLibre sizes its canvas from the container's dimensions at construction time and
    // never re-checks them on its own - if the container is still narrower than its final
    // layout width at that instant (verified live: it consistently was, leaving roughly
    // a third of the card empty), the map stays stuck at that stale size forever.
    const resizeObserver = new ResizeObserver(() => map.resize());
    resizeObserver.observe(containerRef.current);

    return () => {
      resizeObserver.disconnect();
      map.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || neighborhoods.length === 0) return;

    const applyBoundaries = () => {
      const bothSelected = Boolean(selectedIds[0] && selectedIds[1]);

      const featureCollection: GeoJSON.FeatureCollection = {
        type: "FeatureCollection",
        features: neighborhoods.map((n) => ({
          type: "Feature",
          properties: {
            id: n.id,
            name: n.name,
            color: colorFor(n.id, n.overallScore, selectedIds),
            muted: bothSelected && n.id !== selectedIds[0] && n.id !== selectedIds[1],
          },
          geometry: n.boundary,
        })),
      };

      const existingSource = map.getSource(
        "district-boundaries",
      ) as maplibregl.GeoJSONSource | undefined;

      if (existingSource) {
        existingSource.setData(featureCollection);
      } else {
        map.addSource("district-boundaries", {
          type: "geojson",
          data: featureCollection,
        });
        map.addLayer({
          id: "district-fill",
          type: "fill",
          source: "district-boundaries",
          paint: {
            "fill-color": ["get", "color"],
            "fill-opacity": ["case", ["get", "muted"], 0.12, 0.45],
          },
        });
        map.addLayer({
          id: "district-outline",
          type: "line",
          source: "district-boundaries",
          paint: {
            "line-color": ["get", "color"],
            "line-width": 2,
            "line-opacity": ["case", ["get", "muted"], 0.4, 1],
          },
        });
      }

      // Only the two selected districts are the point of the map once a selection
      // exists - fit tightly to them instead of always showing all three at once.
      const selected = new Set(selectedIds.filter((id): id is string => !!id));
      const relevant =
        selected.size > 0
          ? featureCollection.features.filter((f) => selected.has(f.properties!.id))
          : featureCollection.features;

      const bounds = new maplibregl.LngLatBounds();
      let hasBounds = false;
      for (const feature of relevant) {
        // Adalar and Şile are real multi-island districts (MultiPolygon) - skipping them
        // here would leave their bounds contribution out entirely, not just under-fit.
        const polygons =
          feature.geometry.type === "Polygon"
            ? [feature.geometry.coordinates]
            : feature.geometry.type === "MultiPolygon"
              ? feature.geometry.coordinates
              : [];
        for (const rings of polygons) {
          for (const ring of rings) {
            for (const [lng, lat] of ring) {
              bounds.extend([lng, lat]);
              hasBounds = true;
            }
          }
        }
      }
      if (hasBounds) map.fitBounds(bounds, { padding: 60, maxZoom: 13 });
    };

    if (map.isStyleLoaded()) {
      applyBoundaries();
    } else {
      map.once("load", applyBoundaries);
    }
  }, [neighborhoods, selectedIds]);

  return (
    <div ref={containerRef} data-testid="neighborhood-map" className={className} />
  );
}

"use client";

import { useEffect, useRef } from "react";
import * as maplibregl from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import type { NeighborhoodSummary } from "../lib/types";

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

export function NeighborhoodMap({
  neighborhoods,
}: {
  neighborhoods: NeighborhoodSummary[];
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<maplibregl.Map | null>(null);

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;

    const map = new maplibregl.Map({
      container: containerRef.current,
      style: OSM_STYLE,
      center: ISTANBUL_CENTER,
      zoom: 10,
    });
    mapRef.current = map;

    return () => {
      map.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || neighborhoods.length === 0) return;

    const applyBoundaries = () => {
      const featureCollection: GeoJSON.FeatureCollection = {
        type: "FeatureCollection",
        features: neighborhoods.map((n) => ({
          type: "Feature",
          properties: { id: n.id, name: n.name },
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
          paint: { "fill-color": "#2563eb", "fill-opacity": 0.15 },
        });
        map.addLayer({
          id: "district-outline",
          type: "line",
          source: "district-boundaries",
          paint: { "line-color": "#2563eb", "line-width": 2 },
        });
      }

      const bounds = new maplibregl.LngLatBounds();
      let hasBounds = false;
      for (const feature of featureCollection.features) {
        if (feature.geometry.type !== "Polygon") continue;
        for (const ring of feature.geometry.coordinates) {
          for (const [lng, lat] of ring) {
            bounds.extend([lng, lat]);
            hasBounds = true;
          }
        }
      }
      if (hasBounds) map.fitBounds(bounds, { padding: 40 });
    };

    if (map.isStyleLoaded()) {
      applyBoundaries();
    } else {
      map.once("load", applyBoundaries);
    }
  }, [neighborhoods]);

  return (
    <div
      ref={containerRef}
      data-testid="neighborhood-map"
      className="h-96 w-full rounded-lg"
    />
  );
}

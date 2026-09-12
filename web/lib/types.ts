export type DataFreshness = "Fresh" | "Stale" | "Historical";

export interface NeighborhoodSummary {
  id: string;
  name: string;
  boundary: GeoJSON.Geometry;
  overallScore: number | null;
}

export interface DimensionScore {
  score: number | null;
  freshness: DataFreshness | null;
  sourceName: string | null;
  // ISO 8601 string (System.Text.Json's default DateTimeOffset serialization).
  publishedAt: string | null;
}

export interface NeighborhoodScore {
  neighborhoodId: string;
  airQuality: DimensionScore;
  greenSpace: DimensionScore;
  transportation: DimensionScore;
  overall: number | null;
  isComplete: boolean;
}

export interface NeighborhoodComparison {
  a: NeighborhoodScore;
  b: NeighborhoodScore;
}

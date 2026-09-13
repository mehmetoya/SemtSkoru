export type DataFreshness = "Fresh" | "Stale" | "Historical";

export interface NeighborhoodSummary {
  id: string;
  name: string;
  boundary: GeoJSON.Geometry;
  overallScore: number | null;
}

// Matches GET /api/neighborhoods/names: just the id/name pairs, no boundary geometry and no
// scoring join. Use this instead of NeighborhoodSummary/fetchNeighborhoods() when a caller only
// needs a district's display name (see web/app/mahalle/[id]/kart and web/app/karsilastir/kart).
export interface NeighborhoodName {
  id: string;
  name: string;
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
  parking: DimensionScore;
  healthAccess: DimensionScore;
  transitAccess: DimensionScore;
  overall: number | null;
  isComplete: boolean;
}

export interface NeighborhoodComparison {
  a: NeighborhoodScore;
  b: NeighborhoodScore;
}

// Mirrors backend/src/SemtSkoru.Api/Endpoints/AsistanDtos.cs's AssistantOutcomeKind: every
// non-"Ok" value means `recommendations` is empty and `message` carries the Turkish, user-facing
// reason (rate limited, not configured, upstream down, or the model's response wasn't usable) -
// never a fabricated recommendation standing in for a real one.
export type AsistanStatus =
  | "Ok"
  | "InvalidRequest"
  | "NotConfigured"
  | "RateLimited"
  | "Unavailable"
  | "NoUsableRecommendations";

export interface AsistanOneri {
  neighborhoodId: string;
  neighborhoodName: string;
  // The one AI-authored field in this whole response - everything else on `score` is the same
  // real, already-computed data every other page shows.
  reasoning: string;
  score: NeighborhoodScore;
}

export interface AsistanResponse {
  recommendations: AsistanOneri[];
  status: AsistanStatus;
  message: string | null;
}

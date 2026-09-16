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

// The AI "standout traits" summary shown on a district's page (see DistrictSummaryBadge) -
// generated ahead of time by the backend's weekly DistrictSummaryGenerationJob, never live on
// this request. `null` on NeighborhoodScore.summary covers every reason one might not exist yet
// (Gemini not configured, the job hasn't run for this district yet, or no usable summary could be
// grounded in its real scores) - never a fabricated placeholder.
export interface DistrictSummary {
  text: string;
  // ISO 8601 string (System.Text.Json's default DateTimeOffset serialization).
  generatedAt: string;
}

// The AI "what changed" trend summary shown on a district's page (see DistrictTrendBadge) -
// generated ahead of time by the backend's weekly ScoreSnapshotJob, never live on this request.
// `null` on NeighborhoodScore.trend covers every honest reason from DistrictSummary's own list
// PLUS the two reasons unique to trends: no snapshot old enough yet exists to compare against
// (the cold-start state - true for every district for at least a week after this feature first
// deploys, see ScoreSnapshotJob) or nothing about the score changed enough to be worth narrating.
// Never a fabricated placeholder or "check back later" filler - DistrictTrendBadge renders
// nothing at all when this is null.
export interface DistrictTrend {
  text: string;
  // ISO 8601 string (System.Text.Json's default DateTimeOffset serialization).
  generatedAt: string;
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
  summary: DistrictSummary | null;
  trend: DistrictTrend | null;
}

export interface NeighborhoodComparison {
  a: NeighborhoodScore;
  b: NeighborhoodScore;
}

// The AI comparison sentence shown on the compare page (see ComparisonSummaryBadge) - generated
// on demand and cached per district pair by the backend's ComparisonSummaryOrchestrator, never
// pre-generated for all 741 possible pairs the way DistrictSummary is for all 39 districts (see
// that class's own remarks for the quota math). `null` from fetchComparisonSummary covers every
// reason one might not be available yet (Gemini not configured, the live generation call
// failed/timed out/was rate-limited, or the two districts share no dimension either has real data
// for) - never a fabricated placeholder, same contract as DistrictSummary above.
export interface ComparisonSummary {
  text: string;
  // ISO 8601 string (System.Text.Json's default DateTimeOffset serialization).
  generatedAt: string;
}

// Mirrors backend/src/SemtSkoru.Api/Endpoints/AssistantDtos.cs's AssistantOutcomeKind: every
// non-"Ok" value means `recommendations` is empty and `message` carries the Turkish, user-facing
// reason (rate limited, not configured, upstream down, or the model's response wasn't usable) -
// never a fabricated recommendation standing in for a real one.
export type AssistantStatus =
  | "Ok"
  | "InvalidRequest"
  | "NotConfigured"
  | "RateLimited"
  | "Unavailable"
  | "NoUsableRecommendations";

export interface AssistantRecommendation {
  neighborhoodId: string;
  neighborhoodName: string;
  // The one AI-authored field in this whole response - everything else on `score` is the same
  // real, already-computed data every other page shows.
  reasoning: string;
  score: NeighborhoodScore;
}

export interface AssistantResponse {
  recommendations: AssistantRecommendation[];
  status: AssistantStatus;
  message: string | null;
}

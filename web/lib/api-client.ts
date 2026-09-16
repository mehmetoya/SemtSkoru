import type {
  AssistantResponse,
  ComparisonSummary,
  NeighborhoodComparison,
  NeighborhoodName,
  NeighborhoodScore,
  NeighborhoodSummary,
} from "./types";

// Matches the backend's default dev port (backend/src/SemtSkoru.Api/Properties/launchSettings.json).
const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5169";

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`);
  if (!response.ok) {
    throw new Error(`Request to ${path} failed with status ${response.status}`);
  }
  return (await response.json()) as T;
}

export function fetchNeighborhoods(): Promise<NeighborhoodSummary[]> {
  return getJson<NeighborhoodSummary[]>("/api/neighborhoods");
}

// Cheap alternative to fetchNeighborhoods() for callers that only need id->name lookups (e.g.
// the score-card image routes): skips boundary geometry and the full scoring pass entirely.
export function fetchNeighborhoodNames(): Promise<NeighborhoodName[]> {
  return getJson<NeighborhoodName[]>("/api/neighborhoods/names");
}

// `locale` ("tr"/"en") scopes the cached AI district summary/trend embedded in the response (see
// backend/.../DistrictSummaryRepository, DistrictTrendRepository) - every OTHER field here is
// locale-agnostic (the real numeric scores don't change per language), but threading it through
// on every call keeps this one fetch honest about which locale's AI prose it can return.
export function fetchNeighborhoodScore(id: string, locale: string): Promise<NeighborhoodScore> {
  const params = new URLSearchParams({ locale });
  return getJson<NeighborhoodScore>(
    `/api/neighborhoods/${encodeURIComponent(id)}/score?${params.toString()}`,
  );
}

export function fetchNeighborhoodComparison(
  a: string,
  b: string,
): Promise<NeighborhoodComparison> {
  const params = new URLSearchParams({ a, b });
  return getJson<NeighborhoodComparison>(
    `/api/neighborhoods/compare?${params.toString()}`,
  );
}

// A separate request from fetchNeighborhoodComparison above by design - see
// useComparisonSummary.ts. Unwraps the `{ summary }` envelope here so callers just get the
// summary itself (or null) rather than repeating that unwrap at every call site. `locale`
// ("tr"/"en") is part of what's cached server-side per pair (see
// backend/.../ComparisonSummaryOrchestrator) - the first request for a given pair in a given
// locale may cost a live Gemini call even if the other locale is already cached.
export async function fetchComparisonSummary(
  a: string,
  b: string,
  locale: string,
): Promise<ComparisonSummary | null> {
  const params = new URLSearchParams({ a, b, locale });
  const { summary } = await getJson<{ summary: ComparisonSummary | null }>(
    `/api/neighborhoods/compare/summary?${params.toString()}`,
  );
  return summary;
}

// Unlike getJson() above, a non-2xx status here is still meaningful application state, not just
// a failure: POST /api/asistan always returns a structured AssistantResponse body - success or
// not (400/429/502/503, see backend/src/SemtSkoru.Api/Endpoints/AssistantEndpoints.cs) - with a
// Turkish `message` explaining why, so the caller can render that honestly instead of a generic
// "request failed". Only a response that isn't valid JSON at all should surface as a thrown
// error (network failure, or something between the browser and the API neither side produced).
export async function fetchAssistantRecommendations(
  prompt: string,
  locale: string,
): Promise<AssistantResponse> {
  const response = await fetch(`${API_BASE_URL}/api/asistan`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ prompt, locale }),
  });

  return (await response.json()) as AssistantResponse;
}

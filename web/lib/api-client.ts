import type {
  AssistantResponse,
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

export function fetchNeighborhoodScore(id: string): Promise<NeighborhoodScore> {
  return getJson<NeighborhoodScore>(
    `/api/neighborhoods/${encodeURIComponent(id)}/score`,
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

// Unlike getJson() above, a non-2xx status here is still meaningful application state, not just
// a failure: POST /api/asistan always returns a structured AssistantResponse body - success or
// not (400/429/502/503, see backend/src/SemtSkoru.Api/Endpoints/AssistantEndpoints.cs) - with a
// Turkish `message` explaining why, so the caller can render that honestly instead of a generic
// "request failed". Only a response that isn't valid JSON at all should surface as a thrown
// error (network failure, or something between the browser and the API neither side produced).
export async function fetchAssistantRecommendations(prompt: string): Promise<AssistantResponse> {
  const response = await fetch(`${API_BASE_URL}/api/asistan`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ prompt }),
  });

  return (await response.json()) as AssistantResponse;
}

"use client";

import { useQuery } from "@tanstack/react-query";
import { fetchComparisonSummary } from "../api-client";

// Deliberately a SEPARATE query from useCompareNeighborhoods (different queryKey, own
// independent loading/error state) even though both are driven by the same two district ids -
// the score table above it must render immediately from the fast, DB-only /compare response
// without waiting on this one, which may be a live several-second Gemini call the first time this
// exact pair is ever requested (see backend/.../ComparisonSummaryOrchestrator). See
// ComparisonSummaryBadge.tsx for how the three states (loading / a real summary / nothing) render.
export function useComparisonSummary(a: string | undefined, b: string | undefined) {
  return useQuery({
    queryKey: ["comparison-summary", a, b],
    queryFn: () => fetchComparisonSummary(a!, b!),
    enabled: Boolean(a) && Boolean(b),
  });
}

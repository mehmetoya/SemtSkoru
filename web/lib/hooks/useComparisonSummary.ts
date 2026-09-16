"use client";

import { useQuery } from "@tanstack/react-query";
import { fetchComparisonSummary } from "../api-client";

// Deliberately a SEPARATE query from useCompareNeighborhoods (different queryKey, own
// independent loading/error state) even though both are driven by the same two district ids -
// the score table above it must render immediately from the fast, DB-only /compare response
// without waiting on this one, which may be a live several-second Gemini call the first time this
// exact pair is ever requested (see backend/.../ComparisonSummaryOrchestrator). See
// ComparisonSummaryBadge.tsx for how the three states (loading / a real summary / nothing) render.
//
// `locale` is part of the queryKey, not just an argument to queryFn - this is load-bearing, not
// cosmetic. providers.tsx sets a 5-minute staleTime, and the QueryClientProvider lives ABOVE the
// [locale] route segment, so React Query's cache survives a client-side navigation from
// /tr/karsilastir to /en/karsilastir (next-intl's locale switcher is a client-side route change,
// not a full reload). Without locale in the key, a visitor who switches language while a cached
// Turkish result for the same two districts is still fresh would keep seeing that stale Turkish
// text under the English UI for up to 5 minutes - exactly the cross-locale AI-text bug this
// feature exists to avoid, just reintroduced through the query cache.
export function useComparisonSummary(a: string | undefined, b: string | undefined, locale: string) {
  return useQuery({
    queryKey: ["comparison-summary", a, b, locale],
    queryFn: () => fetchComparisonSummary(a!, b!, locale),
    enabled: Boolean(a) && Boolean(b),
  });
}

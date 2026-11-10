"use client";

import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

let browserQueryClient: QueryClient | undefined;

// staleTime: 0 is TanStack Query's own default, which refetches on every mount/refocus/
// reconnect - wasteful here since none of this data (district scores) changes faster than
// daily (see CachedNeighborhoodScoringRepository's remarks backend-side). 5 minutes matches
// the backend's own Cache-Control max-age and the frontend's page-level ISR `revalidate = 300`
// (web/app/[locale]/page.tsx, ilce/[id]/page.tsx) - one number, three layers agreeing on it.
function makeQueryClient() {
  return new QueryClient({
    defaultOptions: { queries: { staleTime: 5 * 60 * 1000 } },
  });
}

function getQueryClient() {
  // Keep server requests isolated and preserve the browser cache across renders.
  if (typeof window === "undefined") return makeQueryClient();
  browserQueryClient ??= makeQueryClient();
  return browserQueryClient;
}

export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={getQueryClient()}>
      {children}
    </QueryClientProvider>
  );
}

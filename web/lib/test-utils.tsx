import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

export function createQueryWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        {children}
      </QueryClientProvider>
    );
  };
}

export const SAMPLE_BOUNDARY: GeoJSON.Geometry = {
  type: "Polygon",
  coordinates: [
    [
      [29.0, 41.0],
      [29.0, 41.01],
      [29.01, 41.01],
      [29.01, 41.0],
      [29.0, 41.0],
    ],
  ],
};

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NextIntlClientProvider } from "next-intl";
import trMessages from "../messages/tr.json";

// Turkish (the app's default locale) is what every existing test's assertions were
// already written against - wrapping with it here keeps every component renderable
// without changing what any test asserts. Components under test that don't use
// next-intl at all are unaffected by the extra provider.
export function createIntlWrapper() {
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <NextIntlClientProvider locale="tr" messages={trMessages}>
        {children}
      </NextIntlClientProvider>
    );
  };
}

export function createQueryWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <NextIntlClientProvider locale="tr" messages={trMessages}>
        <QueryClientProvider client={queryClient}>
          {children}
        </QueryClientProvider>
      </NextIntlClientProvider>
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

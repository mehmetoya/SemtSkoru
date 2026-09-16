import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useComparisonSummary } from "../useComparisonSummary";
import { createQueryWrapper, jsonResponse } from "../../test-utils";

describe("useComparisonSummary", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("unwraps a real cached/generated summary from the { summary } envelope", async () => {
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse({
        summary: {
          text: "Kadıköy hava kalitesinde öne çıkarken, Beşiktaş otoparkta daha güçlü.",
          generatedAt: "2026-09-11T00:00:00Z",
        },
      }),
    );

    const { result } = renderHook(
      () => useComparisonSummary("kadikoy", "besiktas", "tr"),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({
      text: "Kadıköy hava kalitesinde öne çıkarken, Beşiktaş otoparkta daha güçlü.",
      generatedAt: "2026-09-11T00:00:00Z",
    });
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5169/api/neighborhoods/compare/summary?a=kadikoy&b=besiktas&locale=tr",
    );
  });

  it("resolves to null - not an error - when the backend has nothing usable to show", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse({ summary: null }));

    const { result } = renderHook(
      () => useComparisonSummary("kadikoy", "besiktas", "tr"),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it("does not fetch until both district ids are chosen", () => {
    renderHook(() => useComparisonSummary("kadikoy", undefined, "tr"), {
      wrapper: createQueryWrapper(),
    });

    expect(fetch).not.toHaveBeenCalled();
  });

  // THE real edge case this queryKey design exists to prevent: a visitor switches the language
  // toggle (a client-side route change, not a reload - see this hook's own remarks) while a
  // cached "tr" result for the same two districts is still within providers.tsx's 5-minute
  // staleTime. Without locale in the queryKey, React Query would keep serving that stale Turkish
  // text under the new English UI instead of firing a fresh request.
  it("fetches again - a fresh request, not the other locale's cache - when locale changes for the same pair", async () => {
    vi.mocked(fetch).mockImplementation((input) => {
      const url = String(input);
      const text = url.includes("locale=en")
        ? "Kadıköy stands out in air quality."
        : "Kadıköy hava kalitesinde öne çıkıyor.";
      return Promise.resolve(jsonResponse({ summary: { text, generatedAt: "2026-09-11T00:00:00Z" } }));
    });

    const { result, rerender } = renderHook(
      ({ locale }: { locale: string }) => useComparisonSummary("kadikoy", "besiktas", locale),
      { wrapper: createQueryWrapper(), initialProps: { locale: "tr" } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.text).toBe("Kadıköy hava kalitesinde öne çıkıyor.");

    rerender({ locale: "en" });

    await waitFor(() => expect(result.current.data?.text).toBe("Kadıköy stands out in air quality."));
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5169/api/neighborhoods/compare/summary?a=kadikoy&b=besiktas&locale=en",
    );
  });
});

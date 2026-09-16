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
      () => useComparisonSummary("kadikoy", "besiktas"),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({
      text: "Kadıköy hava kalitesinde öne çıkarken, Beşiktaş otoparkta daha güçlü.",
      generatedAt: "2026-09-11T00:00:00Z",
    });
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5169/api/neighborhoods/compare/summary?a=kadikoy&b=besiktas",
    );
  });

  it("resolves to null - not an error - when the backend has nothing usable to show", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse({ summary: null }));

    const { result } = renderHook(
      () => useComparisonSummary("kadikoy", "besiktas"),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it("does not fetch until both district ids are chosen", () => {
    renderHook(() => useComparisonSummary("kadikoy", undefined), {
      wrapper: createQueryWrapper(),
    });

    expect(fetch).not.toHaveBeenCalled();
  });
});

import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useCompareNeighborhoods } from "../useCompareNeighborhoods";
import { createQueryWrapper, jsonResponse } from "../../test-utils";
import type { NeighborhoodComparison } from "../../types";

function emptyDimension() {
  return { score: null, freshness: null, sourceName: null, publishedAt: null };
}

function emptyScore(neighborhoodId: string) {
  return {
    neighborhoodId,
    airQuality: emptyDimension(),
    greenSpace: emptyDimension(),
    transportation: emptyDimension(),
    parking: emptyDimension(),
    healthAccess: emptyDimension(),
    transitAccess: emptyDimension(),
    overall: null,
    isComplete: false,
    summary: null,
  };
}

describe("useCompareNeighborhoods", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("fetches both districts' scores side by side", async () => {
    const comparison: NeighborhoodComparison = {
      a: emptyScore("kadikoy"),
      b: emptyScore("uskudar"),
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(comparison));

    const { result } = renderHook(
      () => useCompareNeighborhoods("kadikoy", "uskudar"),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(comparison);
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5169/api/neighborhoods/compare?a=kadikoy&b=uskudar",
    );
  });

  it("does not fetch until both district ids are chosen", () => {
    renderHook(() => useCompareNeighborhoods("kadikoy", undefined), {
      wrapper: createQueryWrapper(),
    });

    expect(fetch).not.toHaveBeenCalled();
  });

  it("surfaces an error state when the backend rejects the ids", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(null, 400));

    const { result } = renderHook(
      () => useCompareNeighborhoods("kadikoy", "nonexistent"),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

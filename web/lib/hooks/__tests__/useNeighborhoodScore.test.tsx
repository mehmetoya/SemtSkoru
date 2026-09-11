import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useNeighborhoodScore } from "../useNeighborhoodScore";
import { createQueryWrapper, jsonResponse } from "../../test-utils";
import type { NeighborhoodScore } from "../../types";

describe("useNeighborhoodScore", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("fetches the score for the given district", async () => {
    const score: NeighborhoodScore = {
      neighborhoodId: "kadikoy",
      airQuality: {
        score: 83,
        freshness: "Fresh",
        sourceName: "İBB Hava Kalitesi",
        publishedAt: "2026-09-11T00:00:00Z",
      },
      greenSpace: {
        score: 100,
        freshness: "Fresh",
        sourceName: "İBB Yeşil Alan",
        publishedAt: "2025-07-17T00:00:00Z",
      },
      transportation: {
        score: 100,
        freshness: "Fresh",
        sourceName: "İBB Trafik",
        publishedAt: "2025-01-31T00:00:00Z",
      },
      overall: 94,
      isComplete: true,
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(score));

    const { result } = renderHook(() => useNeighborhoodScore("kadikoy"), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(score);
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5169/api/neighborhoods/kadikoy/score",
    );
  });

  it("does not fetch when no district id is given yet", () => {
    renderHook(() => useNeighborhoodScore(undefined), {
      wrapper: createQueryWrapper(),
    });

    expect(fetch).not.toHaveBeenCalled();
  });

  it("surfaces an error state for an unknown district", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(null, 404));

    const { result } = renderHook(() => useNeighborhoodScore("nonexistent"), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

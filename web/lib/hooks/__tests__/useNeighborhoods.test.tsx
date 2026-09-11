import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useNeighborhoods } from "../useNeighborhoods";
import { createQueryWrapper, jsonResponse, SAMPLE_BOUNDARY } from "../../test-utils";
import type { NeighborhoodSummary } from "../../types";

describe("useNeighborhoods", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("starts pending and resolves with the neighborhoods from the API", async () => {
    const neighborhoods: NeighborhoodSummary[] = [
      { id: "kadikoy", name: "Kadıköy", boundary: SAMPLE_BOUNDARY },
      { id: "uskudar", name: "Üsküdar", boundary: SAMPLE_BOUNDARY },
      { id: "besiktas", name: "Beşiktaş", boundary: SAMPLE_BOUNDARY },
    ];
    vi.mocked(fetch).mockResolvedValue(jsonResponse(neighborhoods));

    const { result } = renderHook(() => useNeighborhoods(), {
      wrapper: createQueryWrapper(),
    });

    expect(result.current.isPending).toBe(true);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(neighborhoods);
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5169/api/neighborhoods",
    );
  });

  it("surfaces an error state when the request fails", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(null, 500));

    const { result } = renderHook(() => useNeighborhoods(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

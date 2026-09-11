import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import Home from "../page";
import { createQueryWrapper, jsonResponse, SAMPLE_BOUNDARY } from "../../lib/test-utils";
import type { NeighborhoodSummary } from "../../lib/types";

describe("Home", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("lists the three districts, each linking to its score page", async () => {
    const neighborhoods: NeighborhoodSummary[] = [
      { id: "kadikoy", name: "Kadıköy", boundary: SAMPLE_BOUNDARY },
      { id: "uskudar", name: "Üsküdar", boundary: SAMPLE_BOUNDARY },
      { id: "besiktas", name: "Beşiktaş", boundary: SAMPLE_BOUNDARY },
    ];
    vi.mocked(fetch).mockResolvedValue(jsonResponse(neighborhoods));

    render(<Home />, { wrapper: createQueryWrapper() });

    const kadikoyLink = await screen.findByRole("link", { name: "Kadıköy" });
    expect(kadikoyLink).toHaveAttribute("href", "/mahalle/kadikoy");
    expect(screen.getByRole("link", { name: "Üsküdar" })).toHaveAttribute(
      "href",
      "/mahalle/uskudar",
    );
    expect(screen.getByRole("link", { name: "Beşiktaş" })).toHaveAttribute(
      "href",
      "/mahalle/besiktas",
    );
  });

  it("shows an error message when the district list fails to load", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(null, 500));

    render(<Home />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByText("İlçe listesi yüklenirken bir hata oluştu."),
    ).toBeInTheDocument();
  });
});

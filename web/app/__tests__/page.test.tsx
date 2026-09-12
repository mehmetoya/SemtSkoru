import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import Home from "../page";
import { jsonResponse, SAMPLE_BOUNDARY } from "../../lib/test-utils";
import type { NeighborhoodSummary } from "../../lib/types";

const NEIGHBORHOODS: NeighborhoodSummary[] = [
  { id: "kadikoy", name: "Kadıköy", boundary: SAMPLE_BOUNDARY },
  { id: "uskudar", name: "Üsküdar", boundary: SAMPLE_BOUNDARY },
  { id: "besiktas", name: "Beşiktaş", boundary: SAMPLE_BOUNDARY },
];

// Home is an async Server Component (no hooks, no client-only APIs), so awaiting it and
// rendering the resolved element works fine here - unlike app/mahalle/[id]/page.tsx, which
// also calls next/navigation's notFound() and can't be exercised this way (see Playwright
// e2e instead).
describe("Home", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("lists the three districts, each linking to its score page", async () => {
    vi.mocked(fetch).mockImplementation((input) => {
      const url = String(input);
      if (url.endsWith("/api/neighborhoods")) {
        return Promise.resolve(jsonResponse(NEIGHBORHOODS));
      }
      return Promise.resolve(
        jsonResponse({
          neighborhoodId: "x",
          airQuality: { score: 80, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
          greenSpace: { score: 80, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
          transportation: { score: 80, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
          overall: 80,
          isComplete: true,
        }),
      );
    });

    render(await Home());

    const kadikoyLink = screen.getByRole("link", { name: "Kadıköy" });
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

    render(await Home());

    expect(
      screen.getByText("İlçe listesi yüklenirken bir hata oluştu."),
    ).toBeInTheDocument();
  });
});

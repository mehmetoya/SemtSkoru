import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { NeighborhoodComparisonTable } from "../NeighborhoodComparisonTable";
import { createIntlWrapper } from "../../lib/test-utils";
import type { NeighborhoodScore } from "../../lib/types";

function score(overrides: Partial<NeighborhoodScore>): NeighborhoodScore {
  return {
    neighborhoodId: "test",
    airQuality: { score: 80, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    greenSpace: { score: 90, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    transportation: { score: 70, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    parking: { score: 60, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    healthAccess: { score: 55, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    transitAccess: { score: 65, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    overall: 80,
    isComplete: true,
    summary: null,
    trend: null,
    ...overrides,
  };
}

describe("NeighborhoodComparisonTable", () => {
  it("renders both districts' names and scores side by side", () => {
    render(
      <NeighborhoodComparisonTable
        nameA="Kadıköy"
        nameB="Üsküdar"
        scoreA={score({ overall: 94 })}
        scoreB={score({ overall: 61 })}
      />,
      { wrapper: createIntlWrapper() },
    );

    expect(screen.getByText("Kadıköy")).toBeInTheDocument();
    expect(screen.getByText("Üsküdar")).toBeInTheDocument();
    expect(screen.getByText("94")).toBeInTheDocument();
    expect(screen.getByText("61")).toBeInTheDocument();
  });

  it("labels the delta pill with whichever district is actually ahead", () => {
    // Locks in the "who's ahead" direction so this can't silently desync from the identical
    // ternary in web/app/karsilastir/kart/route.tsx (which renders the same comparison as a
    // downloadable image) - nothing currently asserts this text on either copy.
    const { rerender } = render(
      <NeighborhoodComparisonTable
        nameA="Kadıköy"
        nameB="Üsküdar"
        scoreA={score({ overall: 60 })}
        scoreB={score({ overall: 94 })}
      />,
      { wrapper: createIntlWrapper() },
    );
    expect(screen.getByText("▲ Üsküdar +34")).toBeInTheDocument();

    rerender(
      <NeighborhoodComparisonTable
        nameA="Kadıköy"
        nameB="Üsküdar"
        scoreA={score({ overall: 94 })}
        scoreB={score({ overall: 60 })}
      />,
    );
    expect(screen.getByText("▲ Kadıköy +34")).toBeInTheDocument();
  });

  it("shows 'Veri yok' for a dimension with no data instead of crashing", () => {
    render(
      <NeighborhoodComparisonTable
        nameA="Kadıköy"
        nameB="Beşiktaş"
        scoreA={score({})}
        scoreB={score({
          greenSpace: { score: null, freshness: null, sourceName: null, publishedAt: null },
          isComplete: false,
        })}
      />,
      { wrapper: createIntlWrapper() },
    );

    expect(screen.getByText("Veri yok")).toBeInTheDocument();
  });

  it("shows an em dash when the overall score is unavailable", () => {
    render(
      <NeighborhoodComparisonTable
        nameA="Kadıköy"
        nameB="Beşiktaş"
        scoreA={score({})}
        scoreB={score({ overall: null })}
      />,
      { wrapper: createIntlWrapper() },
    );

    expect(screen.getByText("—")).toBeInTheDocument();
  });
});

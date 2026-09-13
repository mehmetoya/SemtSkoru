import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { NeighborhoodComparisonTable } from "../NeighborhoodComparisonTable";
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
    );

    expect(screen.getByText("Kadıköy")).toBeInTheDocument();
    expect(screen.getByText("Üsküdar")).toBeInTheDocument();
    expect(screen.getByText("94")).toBeInTheDocument();
    expect(screen.getByText("61")).toBeInTheDocument();
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
    );

    expect(screen.getByText("—")).toBeInTheDocument();
  });
});

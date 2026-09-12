import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { NeighborhoodListCard } from "../NeighborhoodListCard";
import type { NeighborhoodScore } from "../../lib/types";

function score(overall: number | null): NeighborhoodScore {
  return {
    neighborhoodId: "kadikoy",
    airQuality: { score: overall, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    greenSpace: { score: overall, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    transportation: { score: overall, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01" },
    overall,
    isComplete: overall !== null,
  };
}

describe("NeighborhoodListCard", () => {
  it("links to the district's detail page and shows its score band", () => {
    render(<NeighborhoodListCard id="kadikoy" name="Kadıköy" score={score(82)} />);

    const link = screen.getByRole("link", { name: "Kadıköy" });
    expect(link).toHaveAttribute("href", "/mahalle/kadikoy");
    expect(screen.getByText("82")).toBeInTheDocument();
    expect(screen.getByText("Genel skor: İyi")).toBeInTheDocument();
  });

  it("shows a fallback when the score failed to load", () => {
    render(<NeighborhoodListCard id="kadikoy" name="Kadıköy" score={null} />);

    expect(screen.getByText("Skor yüklenemedi")).toBeInTheDocument();
  });
});

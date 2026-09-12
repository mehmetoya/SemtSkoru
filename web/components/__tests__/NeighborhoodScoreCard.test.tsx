import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { NeighborhoodScoreCard } from "../NeighborhoodScoreCard";
import type { NeighborhoodScore } from "../../lib/types";

describe("NeighborhoodScoreCard", () => {
  it("renders the district name, overall score, and each dimension's source and date", () => {
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
        freshness: "Historical",
        sourceName: "İBB Trafik",
        publishedAt: "2025-01-31T00:00:00Z",
      },
      overall: 94,
      isComplete: true,
    };

    render(<NeighborhoodScoreCard name="Kadıköy" score={score} />);

    expect(screen.getByText("Kadıköy")).toBeInTheDocument();
    expect(screen.getByText("94")).toBeInTheDocument();
    expect(screen.getByText("Hava Kalitesi")).toBeInTheDocument();
    expect(screen.getByText(/İBB Hava Kalitesi/)).toBeInTheDocument();
    expect(screen.getByText(/17 Temmuz 2025/)).toBeInTheDocument();
    expect(screen.getByText(/Tarihsel/)).toBeInTheDocument();
  });

  it("shows 'Veri yok' and an incomplete notice for a dimension with no data", () => {
    const score: NeighborhoodScore = {
      neighborhoodId: "besiktas",
      airQuality: {
        score: 100,
        freshness: "Fresh",
        sourceName: "İBB Hava Kalitesi",
        publishedAt: "2026-09-11T00:00:00Z",
      },
      greenSpace: { score: null, freshness: null, sourceName: null, publishedAt: null },
      transportation: {
        score: 100,
        freshness: "Fresh",
        sourceName: "İBB Trafik",
        publishedAt: "2025-01-31T00:00:00Z",
      },
      overall: 100,
      isComplete: false,
    };

    render(<NeighborhoodScoreCard name="Beşiktaş" score={score} />);

    expect(screen.getByText("Beşiktaş")).toBeInTheDocument();
    expect(screen.getByText("Veri yok")).toBeInTheDocument();
    expect(
      screen.getByText("Bazı veri boyutları henüz mevcut değil."),
    ).toBeInTheDocument();
  });

  it("sorts dimensions with no data to the bottom", () => {
    const score: NeighborhoodScore = {
      neighborhoodId: "besiktas",
      airQuality: { score: null, freshness: null, sourceName: null, publishedAt: null },
      greenSpace: {
        score: 60,
        freshness: "Fresh",
        sourceName: "s",
        publishedAt: "2025-07-17T00:00:00Z",
      },
      transportation: {
        score: 70,
        freshness: "Fresh",
        sourceName: "s",
        publishedAt: "2025-01-31T00:00:00Z",
      },
      overall: 65,
      isComplete: false,
    };

    render(<NeighborhoodScoreCard name="Beşiktaş" score={score} />);

    const labels = screen.getAllByText(/Hava Kalitesi|Yeşil Alan|Ulaşım/);
    expect(labels.map((el) => el.textContent)).toEqual([
      "Yeşil Alan",
      "Ulaşım",
      "Hava Kalitesi",
    ]);
  });
});

import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { createIntlWrapper, SAMPLE_BOUNDARY } from "../../lib/test-utils";
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
      parking: {
        score: 88,
        freshness: "Fresh",
        sourceName: "İBB İSPARK",
        publishedAt: "2026-09-13T00:00:00Z",
      },
      healthAccess: {
        score: 72,
        freshness: "Fresh",
        sourceName: "İBB Sağlık İndeksi",
        publishedAt: "2024-02-01T00:00:00Z",
      },
      transitAccess: {
        score: 65,
        freshness: "Fresh",
        sourceName: "İETT Otobüs Durakları",
        publishedAt: "2026-03-18T00:00:00Z",
      },
      overall: 94,
      isComplete: true,
      summary: null,
    };

    render(<NeighborhoodScoreCard name="Kadıköy" boundary={SAMPLE_BOUNDARY} score={score} />, {
      wrapper: createIntlWrapper(),
    });

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
      parking: {
        score: 100,
        freshness: "Fresh",
        sourceName: "İBB İSPARK",
        publishedAt: "2026-09-13T00:00:00Z",
      },
      healthAccess: {
        score: 100,
        freshness: "Fresh",
        sourceName: "İBB Sağlık İndeksi",
        publishedAt: "2024-02-01T00:00:00Z",
      },
      transitAccess: {
        score: 100,
        freshness: "Fresh",
        sourceName: "İETT Otobüs Durakları",
        publishedAt: "2026-03-18T00:00:00Z",
      },
      overall: 100,
      isComplete: false,
      summary: null,
    };

    render(<NeighborhoodScoreCard name="Beşiktaş" boundary={SAMPLE_BOUNDARY} score={score} />, {
      wrapper: createIntlWrapper(),
    });

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
      parking: {
        score: 40,
        freshness: "Fresh",
        sourceName: "s",
        publishedAt: "2026-09-13T00:00:00Z",
      },
      healthAccess: {
        score: 45,
        freshness: "Fresh",
        sourceName: "s",
        publishedAt: "2024-02-01T00:00:00Z",
      },
      transitAccess: {
        score: 50,
        freshness: "Fresh",
        sourceName: "s",
        publishedAt: "2026-03-18T00:00:00Z",
      },
      overall: 65,
      isComplete: false,
      summary: null,
    };

    render(<NeighborhoodScoreCard name="Beşiktaş" boundary={SAMPLE_BOUNDARY} score={score} />, {
      wrapper: createIntlWrapper(),
    });

    const labels = screen.getAllByText(/Hava Kalitesi|Yeşil Alan|Ulaşım/);
    expect(labels.map((el) => el.textContent)).toEqual([
      "Yeşil Alan",
      "Ulaşım",
      "Hava Kalitesi",
    ]);
  });

  it("renders without a boundary (e.g. the AI Semt Asistanı's recommendation cards) and as an h2 when asked", () => {
    const score: NeighborhoodScore = {
      neighborhoodId: "kadikoy",
      airQuality: { score: 83, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      greenSpace: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      transportation: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      parking: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      healthAccess: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      transitAccess: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      overall: 97,
      isComplete: true,
      summary: null,
    };

    render(<NeighborhoodScoreCard name="Kadıköy" score={score} headingLevel="h2" />, {
      wrapper: createIntlWrapper(),
    });

    expect(screen.getByRole("heading", { level: 2, name: "Kadıköy" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { level: 1 })).not.toBeInTheDocument();
  });

  it("shows the AI standout-traits summary when one is cached, and nothing when there isn't", () => {
    const baseScore: NeighborhoodScore = {
      neighborhoodId: "kadikoy",
      airQuality: { score: 83, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      greenSpace: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      transportation: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      parking: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      healthAccess: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      transitAccess: { score: 100, freshness: "Fresh", sourceName: "s", publishedAt: "2026-09-11T00:00:00Z" },
      overall: 97,
      isComplete: true,
      summary: null,
    };

    const { rerender } = render(<NeighborhoodScoreCard name="Kadıköy" score={baseScore} />, {
      wrapper: createIntlWrapper(),
    });
    expect(screen.queryByText("Öne Çıkan Özellikler")).not.toBeInTheDocument();

    rerender(
      <NeighborhoodScoreCard
        name="Kadıköy"
        score={{
          ...baseScore,
          summary: { text: "Bu ilçe hava kalitesinde güçlü.", generatedAt: "2026-09-11T00:00:00Z" },
        }}
      />,
    );
    expect(screen.getByText("Bu ilçe hava kalitesinde güçlü.")).toBeInTheDocument();
    expect(screen.getByText(/Google Gemini ile oluşturuldu/)).toBeInTheDocument();
  });
});

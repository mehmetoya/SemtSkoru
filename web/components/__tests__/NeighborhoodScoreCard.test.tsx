import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { NeighborhoodScoreCard } from "../NeighborhoodScoreCard";
import { createQueryWrapper, jsonResponse, SAMPLE_BOUNDARY } from "../../lib/test-utils";
import type { NeighborhoodScore, NeighborhoodSummary } from "../../lib/types";

const neighborhoods: NeighborhoodSummary[] = [
  { id: "kadikoy", name: "Kadıköy", boundary: SAMPLE_BOUNDARY },
  { id: "uskudar", name: "Üsküdar", boundary: SAMPLE_BOUNDARY },
  { id: "besiktas", name: "Beşiktaş", boundary: SAMPLE_BOUNDARY },
];

function mockFetchFor(score: NeighborhoodScore) {
  vi.mocked(fetch).mockImplementation((input) => {
    const url = String(input);
    if (url.endsWith("/api/neighborhoods")) {
      return Promise.resolve(jsonResponse(neighborhoods));
    }
    return Promise.resolve(jsonResponse(score));
  });
}

describe("NeighborhoodScoreCard", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("renders the district name, overall score, and each dimension's source and date", async () => {
    mockFetchFor({
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
    });

    render(<NeighborhoodScoreCard neighborhoodId="kadikoy" />, {
      wrapper: createQueryWrapper(),
    });

    expect(await screen.findByText("Kadıköy")).toBeInTheDocument();
    expect(screen.getByText("94")).toBeInTheDocument();
    expect(screen.getByText("Hava Kalitesi")).toBeInTheDocument();
    expect(screen.getByText(/İBB Hava Kalitesi/)).toBeInTheDocument();
    expect(screen.getByText(/17 Temmuz 2025/)).toBeInTheDocument();
    expect(screen.getByText(/Tarihsel/)).toBeInTheDocument();
  });

  it("shows 'Veri yok' and an incomplete notice for a dimension with no data", async () => {
    mockFetchFor({
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
    });

    render(<NeighborhoodScoreCard neighborhoodId="besiktas" />, {
      wrapper: createQueryWrapper(),
    });

    expect(await screen.findByText("Beşiktaş")).toBeInTheDocument();
    expect(screen.getByText("Veri yok")).toBeInTheDocument();
    expect(
      screen.getByText("Bazı veri boyutları henüz mevcut değil."),
    ).toBeInTheDocument();
  });

  it("shows a loading state before data arrives", () => {
    vi.mocked(fetch).mockImplementation(() => new Promise(() => {}));

    render(<NeighborhoodScoreCard neighborhoodId="kadikoy" />, {
      wrapper: createQueryWrapper(),
    });

    expect(screen.getByRole("status")).toHaveTextContent("Yükleniyor");
  });

  it("shows an error state when the score request fails", async () => {
    vi.mocked(fetch).mockImplementation((input) => {
      const url = String(input);
      if (url.endsWith("/api/neighborhoods")) {
        return Promise.resolve(jsonResponse(neighborhoods));
      }
      return Promise.resolve(jsonResponse(null, 500));
    });

    render(<NeighborhoodScoreCard neighborhoodId="kadikoy" />, {
      wrapper: createQueryWrapper(),
    });

    expect(
      await screen.findByText("Skor yüklenirken bir hata oluştu."),
    ).toBeInTheDocument();
  });
});

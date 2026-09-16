import { describe, expect, it } from "vitest";
import {
  buildComparisonDimensionRows,
  buildDimensionRows,
  buildOverall,
  buildOverallDelta,
  formatGeneratedAt,
  formatImageDate,
  freshnessNote,
} from "../score-card-image";
import type { NeighborhoodScore } from "../types";

function score(overrides: Partial<NeighborhoodScore> = {}): NeighborhoodScore {
  return {
    neighborhoodId: "test",
    airQuality: {
      score: 92,
      freshness: "Fresh",
      sourceName: "İBB Hava Kalitesi",
      publishedAt: "2026-09-11T00:00:00Z",
    },
    greenSpace: {
      score: 41,
      freshness: "Fresh",
      sourceName: "İBB Yeşil Alan",
      publishedAt: "2025-07-17T00:00:00Z",
    },
    transportation: {
      score: 59,
      freshness: "Historical",
      sourceName: "İBB Trafik",
      publishedAt: "2025-01-31T00:00:00Z",
    },
    parking: {
      score: 25,
      freshness: "Fresh",
      sourceName: "İBB İSPARK",
      publishedAt: "2026-09-13T00:00:00Z",
    },
    healthAccess: {
      score: 81,
      freshness: "Fresh",
      sourceName: "İBB Sağlık İndeksi",
      publishedAt: "2024-02-01T00:00:00Z",
    },
    transitAccess: {
      score: 77,
      freshness: "Fresh",
      sourceName: "İETT Otobüs Durakları",
      publishedAt: "2026-03-18T00:00:00Z",
    },
    overall: 62,
    isComplete: true,
    summary: null,
    ...overrides,
  };
}

describe("buildDimensionRows", () => {
  it("maps every dimension to its label, score text, and band", () => {
    const rows = buildDimensionRows(score());
    const airQuality = rows.find((r) => r.key === "airQuality");

    expect(airQuality).toMatchObject({
      label: "Hava Kalitesi",
      hasData: true,
      scoreText: "92",
      bandLabel: "İyi",
    });
    expect(airQuality?.citation).toBe("Kaynak: İBB Hava Kalitesi · 11 Eylül 2026");
  });

  it("renders 'Veri yok' for a null-score dimension instead of guessing or omitting it", () => {
    const rows = buildDimensionRows(
      score({ parking: { score: null, freshness: null, sourceName: null, publishedAt: null } }),
    );
    const parking = rows.find((r) => r.key === "parking");

    expect(parking).toMatchObject({
      hasData: false,
      scoreText: "Veri yok",
      bandLabel: "Veri yok",
      citation: null,
    });
  });

  it("sorts dimensions with data before dimensions without data", () => {
    const rows = buildDimensionRows(
      score({ airQuality: { score: null, freshness: null, sourceName: null, publishedAt: null } }),
    );

    expect(rows[rows.length - 1].key).toBe("airQuality");
    expect(rows.slice(0, -1).every((r) => r.hasData)).toBe(true);
  });

  it("buckets scores into the same bands as the on-page card (>=70 good, >=40 moderate, else poor)", () => {
    const rows = buildDimensionRows(
      score({
        airQuality: { score: 92, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01T00:00:00Z" },
        greenSpace: { score: 55, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01T00:00:00Z" },
        transportation: { score: 10, freshness: "Fresh", sourceName: "s", publishedAt: "2026-01-01T00:00:00Z" },
      }),
    );
    const byKey = Object.fromEntries(rows.map((r) => [r.key, r]));

    expect(byKey.airQuality.colors).toEqual({ text: "#065f46", bg: "#ecfdf5", dot: "#10b981" });
    expect(byKey.greenSpace.colors).toEqual({ text: "#92400e", bg: "#fffbeb", dot: "#f59e0b" });
    expect(byKey.transportation.colors).toEqual({ text: "#991b1b", bg: "#fef2f2", dot: "#ef4444" });
  });

  it("surfaces a freshness note only for non-Fresh sources", () => {
    const rows = buildDimensionRows(score());
    const byKey = Object.fromEntries(rows.map((r) => [r.key, r]));

    expect(byKey.transportation.freshnessNote).toBe("Tarihsel veri");
    expect(byKey.airQuality.freshnessNote).toBeNull();
  });
});

describe("buildOverall", () => {
  it("formats a present score with its band", () => {
    expect(buildOverall(94)).toEqual({
      scoreText: "94",
      bandLabel: "İyi",
      colors: { text: "#065f46", bg: "#ecfdf5", dot: "#10b981" },
    });
  });

  it("shows an em dash for a missing overall score instead of fabricating one", () => {
    expect(buildOverall(null)).toEqual({
      scoreText: "—",
      bandLabel: "Veri yok",
      colors: { text: "#64748b", bg: "#f1f5f9", dot: "#cbd5e1" },
    });
  });
});

describe("freshnessNote", () => {
  it("returns null for Fresh and for missing freshness", () => {
    expect(freshnessNote("Fresh")).toBeNull();
    expect(freshnessNote(null)).toBeNull();
  });

  it("labels Stale and Historical sources in Turkish", () => {
    expect(freshnessNote("Stale")).toBe("Bayat veri");
    expect(freshnessNote("Historical")).toBe("Tarihsel veri");
  });
});

describe("formatImageDate", () => {
  it("renders a long-form Turkish date", () => {
    expect(formatImageDate("2025-07-17T00:00:00Z")).toBe("17 Temmuz 2025");
  });
});

describe("buildComparisonDimensionRows", () => {
  it("keeps the fixed dimension order regardless of which side has data", () => {
    const rows = buildComparisonDimensionRows(
      score({ airQuality: { score: null, freshness: null, sourceName: null, publishedAt: null } }),
      score(),
    );

    expect(rows.map((r) => r.key)).toEqual([
      "airQuality",
      "greenSpace",
      "transportation",
      "parking",
      "healthAccess",
      "transitAccess",
    ]);
  });

  it("shows 'Veri yok' on the side missing data without dropping the row", () => {
    const rows = buildComparisonDimensionRows(
      score({ greenSpace: { score: null, freshness: null, sourceName: null, publishedAt: null } }),
      score(),
    );
    const greenSpace = rows.find((r) => r.key === "greenSpace")!;

    expect(greenSpace.a).toMatchObject({ hasData: false, scoreText: "Veri yok" });
    expect(greenSpace.b).toMatchObject({ hasData: true, scoreText: "41" });
  });

  it("cites whichever side actually has a source when the other is missing data", () => {
    const rows = buildComparisonDimensionRows(
      score({ parking: { score: null, freshness: null, sourceName: null, publishedAt: null } }),
      score(),
    );
    const parking = rows.find((r) => r.key === "parking")!;

    expect(parking.citation).toBe("Kaynak: İBB İSPARK · 13 Eylül 2026");
  });
});

describe("buildOverallDelta", () => {
  it("returns B minus A when both sides have a score", () => {
    expect(buildOverallDelta(61, 94)).toBe(33);
    expect(buildOverallDelta(94, 61)).toBe(-33);
  });

  it("returns null when either side is missing a score, rather than a fabricated delta", () => {
    expect(buildOverallDelta(null, 94)).toBeNull();
    expect(buildOverallDelta(61, null)).toBeNull();
  });

  it("returns null for a tie instead of an empty '+0' pill", () => {
    expect(buildOverallDelta(80, 80)).toBeNull();
  });
});

describe("formatGeneratedAt", () => {
  it("renders the generation timestamp in Turkish, in Istanbul local time", () => {
    // 11:32 UTC == 14:32 Europe/Istanbul (UTC+3, no DST).
    expect(formatGeneratedAt(new Date("2026-09-13T11:32:00Z"))).toBe(
      "13 Eylül 2026 14:32 itibarıyla",
    );
  });
});

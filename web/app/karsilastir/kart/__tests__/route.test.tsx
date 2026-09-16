// @vitest-environment node
//
// Same reasoning as app/mahalle/[id]/kart/__tests__/route.test.tsx: a thin smoke test of
// the real ImageResponse route (real Satori/resvg WASM), not just the pure data-shaping
// helpers already covered by lib/__tests__/score-card-image.test.ts.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { jsonResponse } from "../../../../lib/test-utils";
import type { NeighborhoodName, NeighborhoodScore } from "../../../../lib/types";
import { GET } from "../route";

const NEIGHBORHOOD_NAMES: NeighborhoodName[] = [
  { id: "kadikoy", name: "Kadıköy" },
  { id: "uskudar", name: "Üsküdar" },
];

function score(overrides: Partial<NeighborhoodScore> = {}): NeighborhoodScore {
  return {
    neighborhoodId: "test",
    airQuality: { score: 92, freshness: "Fresh", sourceName: "İBB Hava Kalitesi", publishedAt: "2026-09-11T00:00:00Z" },
    greenSpace: { score: 41, freshness: "Fresh", sourceName: "İBB Yeşil Alan", publishedAt: "2025-07-17T00:00:00Z" },
    transportation: { score: 59, freshness: "Historical", sourceName: "İBB Trafik", publishedAt: "2025-01-31T00:00:00Z" },
    parking: { score: 25, freshness: "Fresh", sourceName: "İBB İSPARK", publishedAt: "2026-09-13T00:00:00Z" },
    healthAccess: { score: 81, freshness: "Fresh", sourceName: "İBB Sağlık İndeksi", publishedAt: "2024-02-01T00:00:00Z" },
    transitAccess: { score: 77, freshness: "Fresh", sourceName: "İETT Otobüs Durakları", publishedAt: "2026-03-18T00:00:00Z" },
    overall: 62,
    isComplete: true,
    summary: null,
    ...overrides,
  };
}

function pngSignature(buffer: Buffer): string {
  return buffer.subarray(0, 8).toString("hex");
}

const realFetch = fetch;

function mockApi(a: NeighborhoodScore, b: NeighborhoodScore) {
  vi.mocked(fetch).mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.includes("/api/neighborhoods/compare")) {
      return jsonResponse({ a, b });
    }
    if (url.endsWith("/api/neighborhoods/names")) {
      return jsonResponse(NEIGHBORHOOD_NAMES);
    }
    return realFetch(input, init);
  });
}

describe("GET /karsilastir/kart", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("renders a real PNG at the declared card size for two fully-scored districts", async () => {
    mockApi(score({ neighborhoodId: "kadikoy" }), score({ neighborhoodId: "uskudar", overall: 72 }));

    const response = await GET(
      new NextRequest("http://test/karsilastir/kart?a=kadikoy&b=uskudar"),
    );

    expect(response.status).toBe(200);
    expect(response.headers.get("content-type")).toBe("image/png");
    const buffer = Buffer.from(await response.arrayBuffer());
    expect(pngSignature(buffer)).toBe("89504e470d0a1a0a");
    expect(buffer.readUInt32BE(16)).toBe(1600);
    expect(buffer.readUInt32BE(20)).toBe(1500);
  });

  it("renders successfully when one side is missing a dimension the other has ('Veri yok', not a crash)", async () => {
    mockApi(
      score({
        neighborhoodId: "kadikoy",
        parking: { score: null, freshness: null, sourceName: null, publishedAt: null },
        isComplete: false,
      }),
      score({ neighborhoodId: "uskudar" }),
    );

    const response = await GET(
      new NextRequest("http://test/karsilastir/kart?a=kadikoy&b=uskudar"),
    );

    expect(response.status).toBe(200);
    const buffer = Buffer.from(await response.arrayBuffer());
    expect(pngSignature(buffer)).toBe("89504e470d0a1a0a");
  });

  it("returns 400 when a district id is missing", async () => {
    const response = await GET(new NextRequest("http://test/karsilastir/kart?a=kadikoy"));
    expect(response.status).toBe(400);
  });

  it("returns 404 when the comparison can't be loaded", async () => {
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.includes("/api/neighborhoods/compare")) return jsonResponse(null, 404);
      if (url.endsWith("/api/neighborhoods/names")) return jsonResponse(NEIGHBORHOOD_NAMES);
      return realFetch(input, init);
    });

    const response = await GET(
      new NextRequest("http://test/karsilastir/kart?a=kadikoy&b=nope"),
    );
    expect(response.status).toBe(404);
  });
});

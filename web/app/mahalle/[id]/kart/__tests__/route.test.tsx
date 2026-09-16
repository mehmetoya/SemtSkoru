// @vitest-environment node
//
// next/og's ImageResponse actually runs Satori + resvg (real WASM, not a mock) - this
// is a thin smoke test of the real route, not just the pure data-shaping helpers
// (see lib/__tests__/score-card-image.test.ts for those). It needs the Node
// environment because ImageResponse/Satori don't work under jsdom.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { jsonResponse } from "../../../../../lib/test-utils";
import type { NeighborhoodName, NeighborhoodScore } from "../../../../../lib/types";
import { GET } from "../route";

// next/og's Node renderer loads its own resvg/yoga WASM via the global fetch at render
// time - stubbing fetch globally (to fake the backend API below) would swallow that
// call too, so anything that isn't our API needs to fall through to the real fetch.
const realFetch = fetch;

const NEIGHBORHOOD_NAMES: NeighborhoodName[] = [{ id: "kadikoy", name: "Kadıköy" }];

function fullScore(): NeighborhoodScore {
  return {
    neighborhoodId: "kadikoy",
    airQuality: { score: 92, freshness: "Fresh", sourceName: "İBB Hava Kalitesi", publishedAt: "2026-09-11T00:00:00Z" },
    greenSpace: { score: 41, freshness: "Fresh", sourceName: "İBB Yeşil Alan", publishedAt: "2025-07-17T00:00:00Z" },
    transportation: { score: 59, freshness: "Historical", sourceName: "İBB Trafik", publishedAt: "2025-01-31T00:00:00Z" },
    parking: { score: 25, freshness: "Fresh", sourceName: "İBB İSPARK", publishedAt: "2026-09-13T00:00:00Z" },
    healthAccess: { score: 81, freshness: "Fresh", sourceName: "İBB Sağlık İndeksi", publishedAt: "2024-02-01T00:00:00Z" },
    transitAccess: { score: 77, freshness: "Fresh", sourceName: "İETT Otobüs Durakları", publishedAt: "2026-03-18T00:00:00Z" },
    overall: 62,
    isComplete: true,
    summary: null,
  };
}

function partialScore(): NeighborhoodScore {
  return {
    ...fullScore(),
    parking: { score: null, freshness: null, sourceName: null, publishedAt: null },
    healthAccess: { score: null, freshness: null, sourceName: null, publishedAt: null },
    transitAccess: { score: null, freshness: null, sourceName: null, publishedAt: null },
    overall: 64,
    isComplete: false,
  };
}

function pngDimensions(buffer: Buffer): { width: number; height: number } {
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

function mockApi(score: NeighborhoodScore | null) {
  vi.mocked(fetch).mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.includes("/api/neighborhoods/") && url.includes("/score")) {
      return score ? jsonResponse(score) : jsonResponse(null, 404);
    }
    if (url.endsWith("/api/neighborhoods/names")) {
      return jsonResponse(NEIGHBORHOOD_NAMES);
    }
    return realFetch(input, init);
  });
}

describe("GET /mahalle/[id]/kart", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("renders a real PNG at the declared card size for a fully-scored district", async () => {
    mockApi(fullScore());

    const response = await GET(new Request("http://test/mahalle/kadikoy/kart"), {
      params: Promise.resolve({ id: "kadikoy" }),
    });

    expect(response.status).toBe(200);
    expect(response.headers.get("content-type")).toBe("image/png");

    const buffer = Buffer.from(await response.arrayBuffer());
    expect(buffer.subarray(0, 8).toString("hex")).toBe("89504e470d0a1a0a"); // PNG signature
    expect(pngDimensions(buffer)).toEqual({ width: 1080, height: 1350 });
  });

  it("renders successfully for a district missing half its dimensions ('Veri yok', not a crash)", async () => {
    mockApi(partialScore());

    const response = await GET(new Request("http://test/mahalle/kadikoy/kart"), {
      params: Promise.resolve({ id: "kadikoy" }),
    });

    expect(response.status).toBe(200);
    const buffer = Buffer.from(await response.arrayBuffer());
    expect(buffer.subarray(0, 8).toString("hex")).toBe("89504e470d0a1a0a");
  });

  it("404s for an unknown district instead of rendering a fabricated card", async () => {
    mockApi(null);

    const response = await GET(new Request("http://test/mahalle/nope/kart"), {
      params: Promise.resolve({ id: "nope" }),
    });

    expect(response.status).toBe(404);
  });
});

import { ImageResponse } from "next/og";
import type { NextRequest } from "next/server";
import { fetchNeighborhoodComparison, fetchNeighborhoodNames } from "../../../lib/api-client";
import {
  buildComparisonDimensionRows,
  buildOverallDelta,
  formatGeneratedAt,
} from "../../../lib/score-card-image";

// Same reasoning as app/ilce/[id]/kart/route.tsx: never cache a card whose entire
// point is to be honest about exactly when it was generated.
export const dynamic = "force-dynamic";

const CARD_WIDTH = 1600;
const CARD_HEIGHT = 1500;

const ACCENT_A = "#1d4ed8"; // blue-700, matches the "Birinci ilçe" accent used on-page
const ACCENT_B = "#a21caf"; // fuchsia-700, matches the "İkinci ilçe" accent used on-page

export async function GET(request: NextRequest) {
  const a = request.nextUrl.searchParams.get("a");
  const b = request.nextUrl.searchParams.get("b");

  if (!a || !b) {
    return new Response("Karşılaştırmak için iki ilçe (a, b) gerekli", { status: 400 });
  }

  const [neighborhoods, comparison] = await Promise.all([
    fetchNeighborhoodNames(),
    fetchNeighborhoodComparison(a, b).catch(() => null),
  ]);

  if (!comparison) {
    return new Response("Karşılaştırma yüklenemedi", { status: 404 });
  }

  const nameA = neighborhoods.find((n) => n.id === a)?.name ?? a;
  const nameB = neighborhoods.find((n) => n.id === b)?.name ?? b;
  const rows = buildComparisonDimensionRows(comparison.a, comparison.b);
  const delta = buildOverallDelta(comparison.a.overall, comparison.b.overall);
  const generatedAt = formatGeneratedAt(new Date());

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          background: "#ffffff",
          padding: "56px 64px",
          fontFamily: "sans-serif",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            paddingBottom: 24,
            borderBottom: "1px solid #e2e8f0",
          }}
        >
          <div style={{ display: "flex", flexDirection: "column", flex: 1 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <div style={{ display: "flex", width: 16, height: 16, borderRadius: 8, background: ACCENT_A }} />
              <div style={{ display: "flex", fontSize: 28, fontWeight: 700, color: ACCENT_A }}>{nameA}</div>
            </div>
            <div style={{ display: "flex", fontSize: 64, fontWeight: 800, color: "#0f172a", marginTop: 6 }}>
              {comparison.a.overall ?? "—"}
            </div>
          </div>
          <div style={{ display: "flex", flexDirection: "column", flex: 1, alignItems: "flex-end" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <div style={{ display: "flex", fontSize: 28, fontWeight: 700, color: ACCENT_B }}>{nameB}</div>
              <div style={{ display: "flex", width: 16, height: 16, borderRadius: 8, background: ACCENT_B }} />
            </div>
            <div style={{ display: "flex", fontSize: 64, fontWeight: 800, color: "#0f172a", marginTop: 6 }}>
              {comparison.b.overall ?? "—"}
            </div>
          </div>
        </div>

        {delta !== null && (
          <div style={{ display: "flex", justifyContent: "center", marginTop: 20 }}>
            <div
              style={{
                display: "flex",
                padding: "10px 24px",
                borderRadius: 999,
                background: "#f1f5f9",
                color: "#475569",
                fontSize: 24,
                fontWeight: 600,
              }}
            >
              {delta > 0 ? `▲ ${nameB} +${delta}` : `▲ ${nameA} +${-delta}`}
            </div>
          </div>
        )}

        <div style={{ display: "flex", flexDirection: "column", marginTop: 24 }}>
          {rows.map((row, index) => (
            <div
              key={row.key}
              style={{
                display: "flex",
                flexDirection: "column",
                paddingTop: 18,
                paddingBottom: 18,
                borderBottom: index < rows.length - 1 ? "1px solid #e2e8f0" : "none",
              }}
            >
              <div style={{ display: "flex", fontSize: 24, fontWeight: 600, color: "#334155", marginBottom: 10 }}>
                {row.label}
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                <ComparisonSideRow accentColor={ACCENT_A} name={nameA} side={row.a} />
                <ComparisonSideRow accentColor={ACCENT_B} name={nameB} side={row.b} />
              </div>
              {row.citation && (
                <div style={{ display: "flex", marginTop: 8, fontSize: 17, color: "#94a3b8" }}>
                  {row.citation}
                </div>
              )}
            </div>
          ))}
        </div>

        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            marginTop: "auto",
            paddingTop: 24,
            borderTop: "1px solid #e2e8f0",
          }}
        >
          <div style={{ display: "flex", fontSize: 26, fontWeight: 800, color: "#1d4ed8" }}>SemtSkoru</div>
          <div style={{ display: "flex", marginTop: 10, fontSize: 20, color: "#475569" }}>{generatedAt}</div>
        </div>
      </div>
    ),
    { width: CARD_WIDTH, height: CARD_HEIGHT },
  );
}

function ComparisonSideRow({
  accentColor,
  name,
  side,
}: {
  accentColor: string;
  name: string;
  side: ReturnType<typeof buildComparisonDimensionRows>[number]["a"];
}) {
  return (
    <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <div style={{ display: "flex", width: 12, height: 12, borderRadius: 6, background: accentColor }} />
        <div style={{ display: "flex", fontSize: 22, fontWeight: 600, color: "#1e293b" }}>{name}</div>
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        {side.hasData && (
          <div style={{ display: "flex", fontSize: 24, fontWeight: 800, color: "#0f172a" }}>{side.scoreText}</div>
        )}
        <div
          style={{
            display: "flex",
            fontSize: 17,
            fontWeight: 600,
            padding: "4px 14px",
            borderRadius: 999,
            background: side.colors.bg,
            color: side.colors.text,
          }}
        >
          {side.bandLabel}
        </div>
      </div>
    </div>
  );
}

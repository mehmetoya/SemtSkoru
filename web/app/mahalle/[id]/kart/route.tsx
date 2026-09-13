import { ImageResponse } from "next/og";
import { fetchNeighborhoodNames, fetchNeighborhoodScore } from "../../../../lib/api-client";
import {
  buildDimensionRows,
  buildOverall,
  formatGeneratedAt,
} from "../../../../lib/score-card-image";

// Never cache: scores update on their own Hangfire cadence (daily/weekly/monthly per
// dimension, see backend Program.cs) and the card's whole point is to be honest about
// exactly when it was generated - a stale cached PNG would defeat that.
export const dynamic = "force-dynamic";

const CARD_WIDTH = 1080;
const CARD_HEIGHT = 1350;

export async function GET(
  _request: Request,
  { params }: RouteContext<"/mahalle/[id]/kart">,
) {
  const { id } = await params;

  const [neighborhoods, score] = await Promise.all([
    fetchNeighborhoodNames(),
    fetchNeighborhoodScore(id).catch(() => null),
  ]);
  const neighborhood = neighborhoods.find((n) => n.id === id);

  if (!neighborhood || !score) {
    return new Response("İlçe bulunamadı", { status: 404 });
  }

  const overall = buildOverall(score.overall);
  const rows = buildDimensionRows(score);
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
          padding: "64px",
          fontFamily: "sans-serif",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <div style={{ display: "flex", fontSize: 56, fontWeight: 800, color: "#0f172a" }}>
            {neighborhood.name}
          </div>
          <div
            style={{
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              justifyContent: "center",
              width: 148,
              height: 148,
              borderRadius: 74,
              background: overall.colors.bg,
              flexShrink: 0,
            }}
          >
            <div style={{ display: "flex", fontSize: 52, fontWeight: 800, color: overall.colors.text }}>
              {overall.scoreText}
            </div>
          </div>
        </div>
        <div
          style={{
            display: "flex",
            fontSize: 20,
            fontWeight: 600,
            textTransform: "uppercase",
            letterSpacing: 1,
            color: "#94a3b8",
            marginTop: 8,
          }}
        >
          Genel skor
        </div>

        {!score.isComplete && (
          <div
            style={{
              display: "flex",
              marginTop: 24,
              padding: "14px 18px",
              borderRadius: 12,
              background: "#fffbeb",
              color: "#92400e",
              fontSize: 22,
            }}
          >
            Bazı veri boyutları henüz mevcut değil.
          </div>
        )}

        <div style={{ display: "flex", flexDirection: "column", marginTop: 28 }}>
          {rows.map((row, index) => (
            <div
              key={row.key}
              style={{
                display: "flex",
                flexDirection: "column",
                paddingTop: 20,
                paddingBottom: 20,
                borderBottom: index < rows.length - 1 ? "1px solid #e2e8f0" : "none",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                <div
                  style={{
                    display: "flex",
                    fontSize: 30,
                    fontWeight: 600,
                    color: row.hasData ? "#334155" : "#94a3b8",
                  }}
                >
                  {row.label}
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
                  {row.hasData && (
                    <div style={{ display: "flex", fontSize: 30, fontWeight: 800, color: "#0f172a" }}>
                      {row.scoreText}
                    </div>
                  )}
                  <div
                    style={{
                      display: "flex",
                      fontSize: 20,
                      fontWeight: 600,
                      padding: "6px 16px",
                      borderRadius: 999,
                      background: row.colors.bg,
                      color: row.colors.text,
                    }}
                  >
                    {row.bandLabel}
                  </div>
                </div>
              </div>
              {(row.citation || row.freshnessNote) && (
                <div
                  style={{
                    display: "flex",
                    marginTop: 6,
                    fontSize: 18,
                    color: "#94a3b8",
                    gap: 10,
                  }}
                >
                  {row.citation && <div style={{ display: "flex" }}>{row.citation}</div>}
                  {row.freshnessNote && (
                    <div
                      style={{
                        display: "flex",
                        padding: "2px 10px",
                        borderRadius: 999,
                        background: "#f1f5f9",
                        color: "#475569",
                        fontSize: 16,
                      }}
                    >
                      {row.freshnessNote}
                    </div>
                  )}
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
            paddingTop: 28,
            borderTop: "1px solid #e2e8f0",
          }}
        >
          <div style={{ display: "flex", fontSize: 26, fontWeight: 800, color: "#1d4ed8" }}>
            SemtSkoru
          </div>
          <div style={{ display: "flex", marginTop: 10, fontSize: 20, color: "#475569" }}>
            {generatedAt}
          </div>
        </div>
      </div>
    ),
    { width: CARD_WIDTH, height: CARD_HEIGHT },
  );
}

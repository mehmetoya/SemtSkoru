import type { DataFreshness, DimensionScore, NeighborhoodScore } from "./types";
import { getScoreBand, SCORE_BAND_LABELS, type ScoreBand } from "./score-band";

// Shared across the single-district and comparison card images (lib/api-client.ts's
// NeighborhoodScore shape), same key/label pairs as NeighborhoodScoreCard.tsx and
// NeighborhoodComparisonTable.tsx use on-page.
export const IMAGE_SCORE_DIMENSIONS = [
  { key: "airQuality", label: "Hava Kalitesi" },
  { key: "greenSpace", label: "Yeşil Alan" },
  { key: "transportation", label: "Ulaşım" },
  { key: "parking", label: "Otopark" },
  { key: "healthAccess", label: "Sağlık Erişimi" },
  { key: "transitAccess", label: "Toplu Taşıma Erişimi" },
] as const satisfies ReadonlyArray<{ key: keyof NeighborhoodScore & string; label: string }>;

export type ImageDimensionKey = (typeof IMAGE_SCORE_DIMENSIONS)[number]["key"];

// Fixed light-mode hex palette for generated images. ImageResponse (Satori) doesn't
// understand Tailwind classes or the `dark:` variant - and a shared/downloaded image
// must look the same regardless of the viewer's site theme - so this mirrors
// lib/score-band.ts's SCORE_BAND_STYLES intent as literal hex instead.
export const IMAGE_BAND_COLORS: Record<
  ScoreBand,
  { text: string; bg: string; dot: string }
> = {
  good: { text: "#065f46", bg: "#ecfdf5", dot: "#10b981" },
  moderate: { text: "#92400e", bg: "#fffbeb", dot: "#f59e0b" },
  poor: { text: "#991b1b", bg: "#fef2f2", dot: "#ef4444" },
  unknown: { text: "#64748b", bg: "#f1f5f9", dot: "#cbd5e1" },
};

const FRESHNESS_LABELS: Record<DataFreshness, string> = {
  Fresh: "Güncel",
  Stale: "Bayat veri",
  Historical: "Tarihsel veri",
};

// Mirrors DataFreshnessBadge.tsx: fresh data needs no callout in the image either, only
// overdue ("Stale") or permanently non-live ("Historical") sources are worth a viewer's
// attention.
export function freshnessNote(freshness: DataFreshness | null): string | null {
  if (freshness === null || freshness === "Fresh") return null;
  return FRESHNESS_LABELS[freshness];
}

export function formatImageDate(iso: string): string {
  return new Date(iso).toLocaleDateString("tr-TR", {
    day: "numeric",
    month: "long",
    year: "numeric",
    timeZone: "Europe/Istanbul",
  });
}

// The visible "generated at" stamp every shared/downloaded card carries, so it can never
// be mistaken for always-current data later - always Istanbul local time regardless of
// the server process's own timezone (UTC on Vercel/Render).
export function formatGeneratedAt(date: Date): string {
  const formatted = new Intl.DateTimeFormat("tr-TR", {
    day: "numeric",
    month: "long",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: "Europe/Istanbul",
  }).format(date);
  return `${formatted} itibarıyla`;
}

export interface ImageDimensionSide {
  hasData: boolean;
  scoreText: string;
  bandLabel: string;
  colors: { text: string; bg: string; dot: string };
}

function buildSide(dimension: DimensionScore): ImageDimensionSide {
  const hasData = dimension.score !== null;
  const band = getScoreBand(dimension.score);

  return {
    hasData,
    scoreText: hasData ? String(dimension.score) : "Veri yok",
    bandLabel: SCORE_BAND_LABELS[band],
    colors: IMAGE_BAND_COLORS[band],
  };
}

function buildCitation(dimension: DimensionScore): string | null {
  return dimension.sourceName && dimension.publishedAt
    ? `Kaynak: ${dimension.sourceName} · ${formatImageDate(dimension.publishedAt)}`
    : null;
}

export interface ImageDimensionRow extends ImageDimensionSide {
  key: ImageDimensionKey;
  label: string;
  citation: string | null;
  freshnessNote: string | null;
}

// Same "has-data first" ordering as NeighborhoodScoreCard.tsx, so a district missing a
// dimension doesn't push it awkwardly into the middle of the card.
export function buildDimensionRows(score: NeighborhoodScore): ImageDimensionRow[] {
  return IMAGE_SCORE_DIMENSIONS.map(({ key, label }) => {
    const dimension = score[key];
    return {
      key,
      label,
      ...buildSide(dimension),
      citation: buildCitation(dimension),
      freshnessNote: freshnessNote(dimension.freshness),
    };
  }).sort((a, b) => Number(b.hasData) - Number(a.hasData));
}

export interface ComparisonDimensionRow {
  key: ImageDimensionKey;
  label: string;
  a: ImageDimensionSide;
  b: ImageDimensionSide;
  citation: string | null;
}

// Unlike buildDimensionRows, this keeps the fixed IMAGE_SCORE_DIMENSIONS order (never
// sorted by data availability) so the two districts' rows always line up on the same
// dimension - matching NeighborhoodComparisonTable.tsx's on-page behavior.
export function buildComparisonDimensionRows(
  scoreA: NeighborhoodScore,
  scoreB: NeighborhoodScore,
): ComparisonDimensionRow[] {
  return IMAGE_SCORE_DIMENSIONS.map(({ key, label }) => {
    const dimA = scoreA[key];
    const dimB = scoreB[key];
    // Same fallback as NeighborhoodComparisonTable.tsx: cite whichever side actually has
    // a source, rather than requiring both.
    const citationSource = dimA.sourceName ? dimA : dimB.sourceName ? dimB : null;

    return {
      key,
      label,
      a: buildSide(dimA),
      b: buildSide(dimB),
      citation: citationSource ? buildCitation(citationSource) : null,
    };
  });
}

// null when either side is missing an overall score, or the two are tied - matching
// NeighborhoodComparisonTable.tsx, which shows no delta pill in either case rather than
// implying a winner that isn't real.
export function buildOverallDelta(
  overallA: number | null,
  overallB: number | null,
): number | null {
  if (overallA === null || overallB === null) return null;
  const delta = overallB - overallA;
  return delta === 0 ? null : delta;
}

export interface ImageOverall {
  scoreText: string;
  bandLabel: string;
  colors: { text: string; bg: string; dot: string };
}

export function buildOverall(overall: number | null): ImageOverall {
  const band = getScoreBand(overall);
  return {
    scoreText: overall === null ? "—" : String(overall),
    bandLabel: SCORE_BAND_LABELS[band],
    colors: IMAGE_BAND_COLORS[band],
  };
}

export type ScoreBand = "good" | "moderate" | "poor" | "unknown";

// Scores are already normalized 0-100 where higher is always better (see backend
// NeighborhoodScoringService) - a flat traffic-light banding applies the same way
// across all three dimensions.
export function getScoreBand(score: number | null | undefined): ScoreBand {
  if (score === null || score === undefined) return "unknown";
  if (score >= 70) return "good";
  if (score >= 40) return "moderate";
  return "poor";
}

export const SCORE_BAND_LABELS: Record<ScoreBand, string> = {
  good: "İyi",
  moderate: "Orta",
  poor: "Düşük",
  unknown: "Veri yok",
};

// text/bg pairs meet WCAG AA contrast at these Tailwind shade combinations.
export const SCORE_BAND_STYLES: Record<
  ScoreBand,
  { text: string; bg: string; dot: string }
> = {
  good: { text: "text-emerald-800", bg: "bg-emerald-50", dot: "bg-emerald-500" },
  moderate: { text: "text-amber-800", bg: "bg-amber-50", dot: "bg-amber-500" },
  poor: { text: "text-red-800", bg: "bg-red-50", dot: "bg-red-500" },
  unknown: { text: "text-slate-500", bg: "bg-slate-100", dot: "bg-slate-300" },
};

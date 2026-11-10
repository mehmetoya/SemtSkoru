// The fixed iteration order for the app's 6 scoring dimensions - shared by every page/
// component that lists them (NeighborhoodScoreCard, NeighborhoodComparisonTable,
// hakkimizda/page.tsx, score-card-image.ts). Labels and methodology copy themselves now
// live in messages/{locale}.json under "Dimensions" (see messages/tr.json's Dimensions
// namespace for the Turkish source of truth, verbatim from
// backend/src/SemtSkoru.Application/Scoring/DimensionScoring.cs) so they can be
// translated - this file only fixes the order and the type-safe key list.
export const DIMENSION_METHODOLOGY_KEYS = [
  "airQuality",
  "greenSpace",
  "transportation",
  "parking",
  "healthAccess",
  "transitAccess",
] as const;

export type DimensionKey = (typeof DIMENSION_METHODOLOGY_KEYS)[number];

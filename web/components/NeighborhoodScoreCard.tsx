import type { DimensionScore, NeighborhoodScore } from "../lib/types";
import { getScoreBand, SCORE_BAND_STYLES } from "../lib/score-band";
import { DIMENSION_METHODOLOGY } from "../lib/dimension-info";
import { DataFreshnessBadge } from "./DataFreshnessBadge";
import { DistrictShapeIcon } from "./DistrictShapeIcon";
import { ScoreBar } from "./ScoreBar";
import { ShareCardButtons } from "./ShareCardButtons";

const DIMENSIONS = [
  { key: "airQuality", label: "Hava Kalitesi" },
  { key: "greenSpace", label: "Yeşil Alan" },
  { key: "transportation", label: "Ulaşım" },
  { key: "parking", label: "Otopark" },
  { key: "healthAccess", label: "Sağlık Erişimi" },
  { key: "transitAccess", label: "Toplu Taşıma Erişimi" },
] as const;

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("tr-TR", {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

function DimensionRow({
  dimensionKey,
  label,
  dimension,
}: {
  dimensionKey: keyof typeof DIMENSION_METHODOLOGY;
  label: string;
  dimension: DimensionScore;
}) {
  const hasData = dimension.score !== null;

  return (
    <div className="border-b border-slate-100 py-4 last:border-0 dark:border-slate-800">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <span className={`font-medium ${hasData ? "text-slate-700 dark:text-slate-300" : "text-slate-400 dark:text-slate-500"}`}>
          {label}
        </span>
        <ScoreBar score={dimension.score} />
      </div>
      <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
        {dimension.sourceName && dimension.publishedAt && (
          <>
            <span>
              Kaynak: {dimension.sourceName} · {formatDate(dimension.publishedAt)}
            </span>
            <DataFreshnessBadge freshness={dimension.freshness} />
          </>
        )}
      </div>
      <details className="mt-1 text-xs text-slate-500 dark:text-slate-400">
        <summary className="cursor-pointer select-none font-medium text-slate-400 hover:text-slate-600 dark:text-slate-500 dark:hover:text-slate-300">
          Nasıl hesaplanıyor?
        </summary>
        <p className="mt-1 max-w-prose">{DIMENSION_METHODOLOGY[dimensionKey]}</p>
      </details>
    </div>
  );
}

export function NeighborhoodScoreCard({
  name,
  boundary,
  score,
  share,
}: {
  name: string;
  boundary: GeoJSON.Geometry;
  score: NeighborhoodScore;
  share?: {
    imageUrl: string;
    fileName: string;
    shareTitle: string;
    shareText: string;
    fallbackUrl: string;
  };
}) {
  const overallBand = getScoreBand(score.overall);
  const overallStyles = SCORE_BAND_STYLES[overallBand];

  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8 dark:border-slate-800 dark:bg-slate-900">
      <div className="flex items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <DistrictShapeIcon boundary={boundary} className={`h-12 w-12 shrink-0 ${overallStyles.text}`} />
          <h1 className="text-2xl font-bold text-slate-900 sm:text-3xl dark:text-slate-100">{name}</h1>
        </div>
        <div
          className={`flex h-16 w-16 shrink-0 flex-col items-center justify-center rounded-full ${overallStyles.bg}`}
        >
          <span className={`text-2xl font-extrabold leading-none ${overallStyles.text}`}>
            {score.overall ?? "—"}
          </span>
        </div>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs font-medium uppercase tracking-wide text-slate-400 dark:text-slate-500">
          Genel skor
        </p>
        {share && <ShareCardButtons {...share} />}
      </div>

      {!score.isComplete && (
        <p className="mt-3 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800 dark:bg-amber-950/50 dark:text-amber-300">
          Bazı veri boyutları henüz mevcut değil.
        </p>
      )}

      <div className="mt-4">
        {[...DIMENSIONS]
          .sort((x, y) => Number(score[y.key].score !== null) - Number(score[x.key].score !== null))
          .map(({ key, label }) => (
            <DimensionRow
              key={key}
              dimensionKey={key}
              label={label}
              dimension={score[key]}
            />
          ))}
      </div>
    </div>
  );
}

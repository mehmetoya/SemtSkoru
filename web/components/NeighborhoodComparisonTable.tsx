import type { DimensionScore, NeighborhoodScore } from "../lib/types";
import { DIMENSION_METHODOLOGY } from "../lib/dimension-info";
import { DataFreshnessBadge } from "./DataFreshnessBadge";
import { ScoreBar } from "./ScoreBar";

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

// The colored dot carries A/B identity via aria-label (an attribute, not rendered
// text) so a district name never repeats as a text node once per dimension row -
// the hero header above is the one place a name is spelled out.
function IdentityRow({
  colorClass,
  name,
  dimension,
}: {
  colorClass: string;
  name: string;
  dimension: DimensionScore;
}) {
  return (
    <div className="flex items-center gap-3">
      <span
        role="img"
        aria-label={`${name} göstergesi`}
        className={`h-2.5 w-2.5 shrink-0 rounded-full ${colorClass}`}
      />
      <div className="min-w-0 flex-1">
        <ScoreBar score={dimension.score} />
      </div>
      <DataFreshnessBadge freshness={dimension.freshness} />
    </div>
  );
}

export function NeighborhoodComparisonTable({
  nameA,
  nameB,
  scoreA,
  scoreB,
}: {
  nameA: string;
  nameB: string;
  scoreA: NeighborhoodScore;
  scoreB: NeighborhoodScore;
}) {
  const delta =
    scoreA.overall !== null && scoreB.overall !== null
      ? scoreB.overall - scoreA.overall
      : null;

  return (
    <div
      data-testid="comparison-result"
      className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6 dark:border-slate-800 dark:bg-slate-900"
    >
      <div className="border-b border-slate-100 pb-5 dark:border-slate-800">
        <p className="text-center text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">
          Genel Skor
        </p>
        <div className="mt-2 flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-blue-700 dark:text-blue-400">
              <span className="h-2 w-2 shrink-0 rounded-full bg-blue-600" aria-hidden="true" />
              <span>{nameA}</span>
            </p>
            <p className="mt-1 text-3xl font-extrabold text-slate-900 dark:text-slate-100">{scoreA.overall ?? "—"}</p>
          </div>
          <div className="min-w-0 flex-1 text-right">
            <p className="flex items-center justify-end gap-1.5 text-xs font-semibold uppercase tracking-wide text-fuchsia-700 dark:text-fuchsia-400">
              <span>{nameB}</span>
              <span className="h-2 w-2 shrink-0 rounded-full bg-fuchsia-600" aria-hidden="true" />
            </p>
            <p className="mt-1 text-3xl font-extrabold text-slate-900 dark:text-slate-100">{scoreB.overall ?? "—"}</p>
          </div>
        </div>
        {delta !== null && delta !== 0 && (
          <p className="mt-3 text-center">
            <span className="inline-block whitespace-nowrap rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold text-slate-600 dark:bg-slate-800 dark:text-slate-300">
              {delta > 0 ? `▲ ${nameB} +${delta}` : `▲ ${nameA} +${-delta}`}
            </span>
          </p>
        )}
      </div>

      <div>
        {DIMENSIONS.map(({ key, label }) => {
          const dimA = scoreA[key];
          const dimB = scoreB[key];
          const citation = dimA.sourceName ? dimA : dimB.sourceName ? dimB : null;

          return (
            <div key={key} className="border-b border-slate-100 py-4 last:border-0 dark:border-slate-800">
              <p className="mb-2 text-sm font-medium text-slate-700 dark:text-slate-300">{label}</p>
              <div className="space-y-2">
                <IdentityRow colorClass="bg-blue-600" name={nameA} dimension={dimA} />
                <IdentityRow colorClass="bg-fuchsia-600" name={nameB} dimension={dimB} />
              </div>
              {citation && citation.publishedAt && (
                <p className="mt-2 text-xs text-slate-400 dark:text-slate-500">
                  Kaynak: {citation.sourceName} · {formatDate(citation.publishedAt)}
                </p>
              )}
              <details className="mt-2 text-xs text-slate-500 dark:text-slate-400">
                <summary className="cursor-pointer select-none font-medium text-slate-400 hover:text-slate-600 dark:text-slate-500 dark:hover:text-slate-300">
                  Nasıl hesaplanıyor?
                </summary>
                <p className="mt-1 max-w-prose">{DIMENSION_METHODOLOGY[key]}</p>
              </details>
            </div>
          );
        })}
      </div>
    </div>
  );
}

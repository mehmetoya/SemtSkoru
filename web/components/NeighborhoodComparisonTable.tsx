import { useFormatter, useTranslations } from "next-intl";
import type { ComparisonSummary, DimensionScore, NeighborhoodScore } from "../lib/types";
import { ComparisonSummaryBadge } from "./ComparisonSummaryBadge";
import { DataFreshnessBadge } from "./DataFreshnessBadge";
import { ScoreBar } from "./ScoreBar";
import { ShareCardButtons } from "./ShareCardButtons";

const DIMENSION_KEYS = [
  "airQuality",
  "greenSpace",
  "transportation",
  "parking",
  "healthAccess",
  "transitAccess",
] as const;

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
  const t = useTranslations("NeighborhoodComparisonTable");
  return (
    <div className="flex items-center gap-3">
      <span
        role="img"
        aria-label={t("indicatorAria", { name })}
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
  share,
  comparisonSummary,
  isComparisonSummaryLoading = false,
}: {
  nameA: string;
  nameB: string;
  scoreA: NeighborhoodScore;
  scoreB: NeighborhoodScore;
  share?: {
    imageUrl: string;
    fileName: string;
    shareTitle: string;
    shareText: string;
    fallbackUrl: string;
  };
  // Optional: only CompareClient (which has a live useComparisonSummary() fetch to hand in) sets
  // these. A caller that doesn't pass either (e.g. a future consumer that just wants the score
  // table) simply never renders the AI badge, matching `share` above's own optional pattern.
  comparisonSummary?: ComparisonSummary | null;
  isComparisonSummaryLoading?: boolean;
}) {
  const t = useTranslations("NeighborhoodComparisonTable");
  const tDimensions = useTranslations("Dimensions");
  const format = useFormatter();

  function formatDate(iso: string): string {
    return format.dateTime(new Date(iso), { day: "numeric", month: "long", year: "numeric" });
  }

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
          {t("overallScoreLabel")}
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
        <ComparisonSummaryBadge summary={comparisonSummary} isLoading={isComparisonSummaryLoading} />

        {share && (
          <div className="mt-4 flex justify-center">
            <ShareCardButtons {...share} />
          </div>
        )}
      </div>

      <div>
        {DIMENSION_KEYS.map((key) => {
          const dimA = scoreA[key];
          const dimB = scoreB[key];
          const citation = dimA.sourceName ? dimA : dimB.sourceName ? dimB : null;

          return (
            <div key={key} className="border-b border-slate-100 py-4 last:border-0 dark:border-slate-800">
              <p className="mb-2 text-sm font-medium text-slate-700 dark:text-slate-300">{tDimensions(key)}</p>
              <div className="space-y-2">
                <IdentityRow colorClass="bg-blue-600" name={nameA} dimension={dimA} />
                <IdentityRow colorClass="bg-fuchsia-600" name={nameB} dimension={dimB} />
              </div>
              {citation && citation.publishedAt && (
                <p className="mt-2 text-xs text-slate-400 dark:text-slate-500">
                  {t("sourceLabel", { source: citation.sourceName ?? "", date: formatDate(citation.publishedAt) })}
                </p>
              )}
              <details className="mt-2 text-xs text-slate-500 dark:text-slate-400">
                <summary className="cursor-pointer select-none font-medium text-slate-400 hover:text-slate-600 dark:text-slate-500 dark:hover:text-slate-300">
                  {t("howCalculated")}
                </summary>
                <p className="mt-1 max-w-prose">{tDimensions(`methodology.${key}`)}</p>
              </details>
            </div>
          );
        })}
      </div>
    </div>
  );
}

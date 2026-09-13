import { getScoreBand, SCORE_BAND_LABELS, SCORE_BAND_STYLES } from "../lib/score-band";

const BAND_FILL: Record<string, string> = {
  good: "bg-emerald-500",
  moderate: "bg-amber-500",
  poor: "bg-red-500",
  unknown: "bg-slate-200",
};

// A labeled 0-100 bar: the fill carries the color, the number stays neutral ink,
// and the band word is repeated in its own chip so state is never color-alone.
export function ScoreBar({ score }: { score: number | null }) {
  const band = getScoreBand(score);
  const styles = SCORE_BAND_STYLES[band];

  return (
    <div className={`flex items-center gap-3 ${score === null ? "opacity-50" : ""}`}>
      <div className="h-2 max-w-40 flex-1 rounded-full bg-slate-100 dark:bg-slate-800">
        {score !== null && (
          <div
            className={`h-2 rounded-full ${BAND_FILL[band]}`}
            style={{ width: `${Math.max(score, 4)}%` }}
          />
        )}
      </div>
      <span className="w-8 shrink-0 text-right text-sm font-bold text-slate-900 dark:text-slate-100">
        {score ?? "—"}
      </span>
      <span
        className={`shrink-0 rounded-full px-2 py-0.5 text-xs font-medium ${styles.bg} ${styles.text}`}
      >
        {SCORE_BAND_LABELS[band]}
      </span>
    </div>
  );
}

import { Link } from "../i18n/navigation";
import type { NeighborhoodSummary } from "../lib/types";
import { getScoreBand, SCORE_BAND_STYLES } from "../lib/score-band";
import { DistrictShapeIcon } from "./DistrictShapeIcon";

export function HighlightCard({
  label,
  neighborhood,
}: {
  label: string;
  neighborhood: NeighborhoodSummary & { overallScore: number };
}) {
  const band = getScoreBand(neighborhood.overallScore);
  const styles = SCORE_BAND_STYLES[band];

  return (
    <Link
      href={`/ilce/${neighborhood.id}`}
      className="flex items-center gap-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-slate-300 hover:shadow-md dark:border-slate-800 dark:bg-slate-900 dark:hover:border-slate-700"
    >
      <DistrictShapeIcon boundary={neighborhood.boundary} className={`h-10 w-10 shrink-0 ${styles.text}`} />
      <div className="min-w-0">
        <p className="text-xs font-medium uppercase tracking-wide text-slate-400 dark:text-slate-500">{label}</p>
        <p className="truncate font-semibold text-slate-900 dark:text-slate-100">{neighborhood.name}</p>
      </div>
      <div
        className={`ml-auto flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-sm font-bold ${styles.bg} ${styles.text}`}
      >
        {neighborhood.overallScore}
      </div>
    </Link>
  );
}

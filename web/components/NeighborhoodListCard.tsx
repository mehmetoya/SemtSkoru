import Link from "next/link";
import type { NeighborhoodScore } from "../lib/types";
import { getScoreBand, SCORE_BAND_LABELS, SCORE_BAND_STYLES } from "../lib/score-band";

export function NeighborhoodListCard({
  id,
  name,
  score,
}: {
  id: string;
  name: string;
  score: NeighborhoodScore | null;
}) {
  const band = getScoreBand(score?.overall);
  const styles = SCORE_BAND_STYLES[band];

  return (
    <li className="group rounded-xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-slate-300 hover:shadow-md">
      <div className="flex items-center justify-between gap-4">
        <Link
          href={`/mahalle/${id}`}
          className="text-lg font-semibold text-slate-900 group-hover:text-blue-700"
        >
          {name}
        </Link>
        <div
          className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-base font-bold ${styles.bg} ${styles.text}`}
          aria-hidden="true"
        >
          {score?.overall ?? "—"}
        </div>
      </div>
      <p className={`mt-2 text-sm font-medium ${styles.text}`}>
        {score ? `Genel skor: ${SCORE_BAND_LABELS[band]}` : "Skor yüklenemedi"}
      </p>
    </li>
  );
}

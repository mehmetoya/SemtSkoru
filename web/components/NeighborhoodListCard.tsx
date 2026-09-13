import Link from "next/link";
import { getScoreBand, SCORE_BAND_LABELS, SCORE_BAND_STYLES } from "../lib/score-band";
import { DistrictShapeIcon } from "./DistrictShapeIcon";

export function NeighborhoodListCard({
  id,
  name,
  boundary,
  overallScore,
}: {
  id: string;
  name: string;
  boundary: GeoJSON.Geometry;
  overallScore: number | null;
}) {
  const band = getScoreBand(overallScore);
  const styles = SCORE_BAND_STYLES[band];

  return (
    <li className="group rounded-xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-slate-300 hover:shadow-md dark:border-slate-800 dark:bg-slate-900 dark:hover:border-slate-700">
      <div className="flex items-center justify-between gap-4">
        <Link
          href={`/mahalle/${id}`}
          className="text-lg font-semibold text-slate-900 group-hover:text-blue-700 dark:text-slate-100 dark:group-hover:text-blue-400"
        >
          {name}
        </Link>
        <DistrictShapeIcon boundary={boundary} className={`h-10 w-10 shrink-0 ${styles.text}`} />
      </div>
      <div className="mt-2 flex items-center justify-between">
        <p className={`text-sm font-medium ${styles.text}`}>
          {overallScore === null ? "Skor yüklenemedi" : `Genel skor: ${SCORE_BAND_LABELS[band]}`}
        </p>
        <div
          className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-sm font-bold ${styles.bg} ${styles.text}`}
          aria-hidden="true"
        >
          {overallScore ?? "—"}
        </div>
      </div>
    </li>
  );
}

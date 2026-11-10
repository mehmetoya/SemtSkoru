import { useTranslations } from "next-intl";
import { Link } from "../i18n/navigation";
import { getScoreBand, SCORE_BAND_STYLES } from "../lib/score-band";
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
  const t = useTranslations("NeighborhoodListCard");
  const tBand = useTranslations("ScoreBand");
  const band = getScoreBand(overallScore);
  const styles = SCORE_BAND_STYLES[band];

  return (
    // The whole card is one Link (not just the name) - nothing else inside it is independently
    // interactive, so there's no reason to make a visitor aim for one line of text instead of
    // anywhere on the card (real user feedback: this was reported as confusing on mobile).
    <li className="group rounded-xl border border-slate-200 bg-white shadow-sm transition hover:border-slate-300 hover:shadow-md dark:border-slate-800 dark:bg-slate-900 dark:hover:border-slate-700">
      <Link href={`/ilce/${id}`} className="block p-5">
        <div className="flex items-center justify-between gap-4">
          <span className="text-lg font-semibold text-slate-900 group-hover:text-blue-700 dark:text-slate-100 dark:group-hover:text-blue-400">
            {name}
          </span>
          <DistrictShapeIcon boundary={boundary} className={`h-10 w-10 shrink-0 ${styles.text}`} />
        </div>
        <div className="mt-2 flex items-center justify-between">
          <p className={`text-sm font-medium ${styles.text}`}>
            {overallScore === null ? t("scoreFailedToLoad") : t("overallScoreWithBand", { band: tBand(band) })}
          </p>
          <div
            className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-sm font-bold ${styles.bg} ${styles.text}`}
            aria-hidden="true"
          >
            {overallScore ?? "—"}
          </div>
        </div>
      </Link>
    </li>
  );
}

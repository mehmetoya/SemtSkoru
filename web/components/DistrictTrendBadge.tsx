import { useTranslations } from "next-intl";
import type { DistrictTrend } from "../lib/types";

// Same violet-tinted "AI-attributed content" visual language as DistrictSummaryBadge (and
// AssistantClient's disclosure pill/AssistantRecommendationCard's "Neden X?" box) - a visitor
// who already recognizes one of these recognizes all of them. Renders nothing at all (not an
// error, not a "check back later" placeholder) when there's no cached trend yet - see
// ScoreSnapshotJob/DistrictTrendService for the several honest reasons that can be true, the
// most important of which is the cold-start one: this feature has no persisted score history at
// all until this app's first-ever weekly snapshot run, and needs a baseline snapshot at least a
// week old before it can honestly say anything - so EVERY district shows nothing here for at
// least a week right after this feature ships, and that is the expected, correct state, not a
// bug to work around.
export function DistrictTrendBadge({ trend }: { trend: DistrictTrend | null }) {
  const t = useTranslations("DistrictTrendBadge");
  const tCommon = useTranslations("Common");

  if (!trend) {
    return null;
  }

  return (
    <div className="mt-4 rounded-xl border border-violet-200 bg-violet-50 px-4 py-3 dark:border-violet-900 dark:bg-violet-950/40">
      <p className="text-xs font-semibold uppercase tracking-wide text-violet-700 dark:text-violet-300">
        {t("heading")}
      </p>
      <p className="mt-1 text-sm text-violet-900 dark:text-violet-200">{trend.text}</p>
      <p className="mt-2 text-[11px] text-violet-500 dark:text-violet-400">{tCommon("aiAttribution")}</p>
    </div>
  );
}

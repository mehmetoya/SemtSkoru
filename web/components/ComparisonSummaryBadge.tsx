import { useTranslations } from "next-intl";
import type { ComparisonSummary } from "../lib/types";

// Same violet-tinted "AI-attributed content" visual language as DistrictSummaryBadge (and
// AssistantRecommendationCard's "Neden X?" box) - see that component's own remarks for why this
// app deliberately reuses one look for every AI-authored surface. Unlike DistrictSummaryBadge,
// this one DOES need to know about a loading state: a district's own summary is always either
// already sitting in the DB (pre-generated weekly) or not, but a comparison pair may genuinely
// never have been requested before, in which case the backend makes a live several-second Gemini
// call on this very request (see ComparisonSummaryOrchestrator) - a visitor staring at a blank
// gap for that long with no feedback would look broken. Renders nothing at all (not an error, not
// a placeholder) once loading has settled and there's still no summary - covers every honest
// reason one might not exist (Gemini not configured, the live call failed/timed out/was
// rate-limited, or the two districts share no dimension either has real data for) - exactly the
// same "the rest of the page still works fully" contract DistrictSummaryBadge already has.
export function ComparisonSummaryBadge({
  summary,
  isLoading,
}: {
  summary: ComparisonSummary | null | undefined;
  isLoading: boolean;
}) {
  const t = useTranslations("ComparisonSummaryBadge");
  const tCommon = useTranslations("Common");

  if (!isLoading && !summary) {
    return null;
  }

  return (
    <div className="mt-4 rounded-xl border border-violet-200 bg-violet-50 px-4 py-3 dark:border-violet-900 dark:bg-violet-950/40">
      <p className="text-xs font-semibold uppercase tracking-wide text-violet-700 dark:text-violet-300">
        {t("heading")}
      </p>
      {summary ? (
        <>
          <p className="mt-1 text-sm text-violet-900 dark:text-violet-200">{summary.text}</p>
          <p className="mt-2 text-[11px] text-violet-500 dark:text-violet-400">{tCommon("aiAttribution")}</p>
        </>
      ) : (
        <p role="status" className="mt-1 animate-pulse text-sm text-violet-700 dark:text-violet-300">
          {t("loading")}
        </p>
      )}
    </div>
  );
}

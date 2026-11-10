import { useTranslations } from "next-intl";
import type { DataFreshness } from "../lib/types";

// Fresh data needs no badge -- only call this out when the source is overdue (Stale) or
// permanently non-live by design (Historical), per SPEC.md's stale-data warning boundary.
export function DataFreshnessBadge({
  freshness,
}: {
  freshness: DataFreshness | null;
}) {
  const t = useTranslations("DataFreshness");

  if (freshness === null || freshness === "Fresh") {
    return null;
  }

  const isStale = freshness === "Stale";

  return (
    <span
      className={
        isStale
          ? "rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-950/50 dark:text-amber-300"
          : "rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-300"
      }
    >
      {isStale && "⚠ "}
      {t(freshness)}
    </span>
  );
}

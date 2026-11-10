"use client";

import dynamic from "next/dynamic";
import { useLocale, useTranslations } from "next-intl";
import { useNeighborhoods } from "../lib/hooks/useNeighborhoods";
import { useCompareNeighborhoods } from "../lib/hooks/useCompareNeighborhoods";
import { useComparisonSummary } from "../lib/hooks/useComparisonSummary";
import { NeighborhoodComparisonTable } from "./NeighborhoodComparisonTable";
import { DistrictPicker } from "./DistrictPicker";
import { SITE_URL } from "../lib/site";
import { useState } from "react";

function MapLoadingFallback() {
  const t = useTranslations("Compare");
  return (
    <div className="flex h-96 w-full animate-pulse items-center justify-center bg-slate-100 text-sm text-slate-400 lg:h-150 dark:bg-slate-800 dark:text-slate-500">
      {t("mapLoading")}
    </div>
  );
}

// MapLibre GL is a large library (~1MB parsed) that only this page needs and that only ever
// runs in the browser (it draws to a <canvas> via WebGL and touches `window` at import time -
// see NeighborhoodMap.tsx's setWorkerUrl call). Loading it through next/dynamic with ssr:false
// keeps it out of both the server render and this component's own chunk, so it fetches and
// parses in parallel with (not blocking) everything else on this page becoming interactive.
const NeighborhoodMap = dynamic(
  () => import("./NeighborhoodMap").then((mod) => mod.NeighborhoodMap),
  { ssr: false, loading: MapLoadingFallback },
);

export function CompareClient() {
  const t = useTranslations("Compare");
  const tBand = useTranslations("ScoreBand");
  const locale = useLocale();
  const { data: neighborhoods } = useNeighborhoods();
  const [a, setA] = useState<string | undefined>(undefined);
  const [b, setB] = useState<string | undefined>(undefined);

  const {
    data: comparison,
    isPending,
    isError,
  } = useCompareNeighborhoods(a, b);

  // Deliberately a separate fetch from the score comparison above (see useComparisonSummary's own
  // remarks) - a slow or failed AI summary must never hold up or error the score table itself.
  const { data: comparisonSummary, isPending: isComparisonSummaryPending } = useComparisonSummary(a, b, locale);

  const nameOf = (id: string | undefined) =>
    neighborhoods?.find((n) => n.id === id)?.name ?? "";

  function handleSelectDistrict(id: string) {
    if (a === id) {
      setA(undefined);
    } else if (b === id) {
      setB(undefined);
    } else if (!a) {
      setA(id);
    } else if (!b) {
      setB(id);
    } else {
      setA(id);
    }
  }

  return (
    <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
      {/* Map first in source order (keyboard/reader users reach the primary content
          before the picker controls), but sits below the pickers on small screens
          where a giant map before any selection wastes the first scroll. */}
      <div className="order-2 lg:order-1">
        <div className="mb-3 flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-slate-500 dark:text-slate-400">
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-blue-600" /> {t("firstDistrict")}
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-fuchsia-600" /> {t("secondDistrict")}
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-emerald-500" /> {tBand("good")}
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-amber-500" /> {tBand("moderate")}
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-red-500" /> {tBand("poor")}
          </span>
          <span>{t("mapHint")}</span>
        </div>
        <div className="overflow-hidden rounded-2xl border border-slate-200 shadow-sm dark:border-slate-800">
          <NeighborhoodMap
            neighborhoods={neighborhoods ?? []}
            selectedIds={[a, b]}
            onSelectDistrict={handleSelectDistrict}
            className="h-96 w-full lg:h-150"
          />
        </div>
      </div>

      <div className="order-1 flex flex-col gap-4 lg:order-2 lg:sticky lg:top-6">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-1">
          <DistrictPicker
            label={t("firstDistrict")}
            accent="a"
            neighborhoods={neighborhoods ?? []}
            excludeId={b}
            value={a}
            onChange={setA}
          />
          <DistrictPicker
            label={t("secondDistrict")}
            accent="b"
            neighborhoods={neighborhoods ?? []}
            excludeId={a}
            value={b}
            onChange={setB}
          />
        </div>

        {a && b && isPending && (
          <p role="status" className="text-sm text-slate-500 dark:text-slate-400">
            {t("loadingResult")}
          </p>
        )}
        {isError && (
          <p className="text-sm text-red-700 dark:text-red-400">{t("errorLoadingComparison")}</p>
        )}
        {comparison && a && b && (
          <NeighborhoodComparisonTable
            nameA={nameOf(a)}
            nameB={nameOf(b)}
            scoreA={comparison.a}
            scoreB={comparison.b}
            comparisonSummary={comparisonSummary}
            isComparisonSummaryLoading={isComparisonSummaryPending}
            share={{
              imageUrl: `/karsilastir/kart?${new URLSearchParams({ a, b })}`,
              fileName: `semtskoru-karsilastirma-${a}-${b}.png`,
              shareTitle: t("shareTitle", { nameA: nameOf(a), nameB: nameOf(b) }),
              shareText: t("shareText", { nameA: nameOf(a), nameB: nameOf(b) }),
              // The comparison page itself doesn't persist the a/b selection in the URL,
              // so (unlike the single-district card, which can safely link back to its
              // own always-current page) the honest fallback link here is the generated
              // image itself - a stable, timestamped snapshot of exactly this comparison,
              // and (like that image route) deliberately locale-agnostic.
              fallbackUrl: `${SITE_URL}/karsilastir/kart?${new URLSearchParams({ a, b })}`,
            }}
          />
        )}
      </div>
    </div>
  );
}

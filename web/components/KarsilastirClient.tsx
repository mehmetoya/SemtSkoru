"use client";

import { useNeighborhoods } from "../lib/hooks/useNeighborhoods";
import { useCompareNeighborhoods } from "../lib/hooks/useCompareNeighborhoods";
import { NeighborhoodComparisonTable } from "./NeighborhoodComparisonTable";
import { NeighborhoodMap } from "./NeighborhoodMap";
import { DistrictPicker } from "./DistrictPicker";
import { SITE_URL } from "../lib/site";
import { useState } from "react";

export function KarsilastirClient() {
  const { data: neighborhoods } = useNeighborhoods();
  const [a, setA] = useState<string | undefined>(undefined);
  const [b, setB] = useState<string | undefined>(undefined);

  const {
    data: comparison,
    isPending,
    isError,
  } = useCompareNeighborhoods(a, b);

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
            <span className="h-2.5 w-2.5 rounded-full bg-blue-600" /> Birinci ilçe
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-fuchsia-600" /> İkinci ilçe
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-emerald-500" /> İyi
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-amber-500" /> Orta
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-red-500" /> Düşük
          </span>
          <span>Haritadan bir ilçeye tıklayarak da seçebilirsin.</span>
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
            label="Birinci ilçe"
            accent="a"
            neighborhoods={neighborhoods ?? []}
            excludeId={b}
            value={a}
            onChange={setA}
          />
          <DistrictPicker
            label="İkinci ilçe"
            accent="b"
            neighborhoods={neighborhoods ?? []}
            excludeId={a}
            value={b}
            onChange={setB}
          />
        </div>

        {a && b && isPending && (
          <p role="status" className="text-sm text-slate-500 dark:text-slate-400">
            Yükleniyor…
          </p>
        )}
        {isError && (
          <p className="text-sm text-red-700 dark:text-red-400">Karşılaştırma yüklenirken bir hata oluştu.</p>
        )}
        {comparison && a && b && (
          <NeighborhoodComparisonTable
            nameA={nameOf(a)}
            nameB={nameOf(b)}
            scoreA={comparison.a}
            scoreB={comparison.b}
            share={{
              imageUrl: `/karsilastir/kart?a=${a}&b=${b}`,
              fileName: `semtskoru-karsilastirma-${a}-${b}.png`,
              shareTitle: `${nameOf(a)} - ${nameOf(b)} Karşılaştırması | SemtSkoru`,
              shareText: `${nameOf(a)} ve ${nameOf(b)} ilçelerinin SemtSkoru karşılaştırmasını incele.`,
              // The comparison page itself doesn't persist the a/b selection in the URL,
              // so (unlike the single-district card, which can safely link back to its
              // own always-current page) the honest fallback link here is the generated
              // image itself - a stable, timestamped snapshot of exactly this comparison.
              fallbackUrl: `${SITE_URL}/karsilastir/kart?a=${a}&b=${b}`,
            }}
          />
        )}
      </div>
    </div>
  );
}

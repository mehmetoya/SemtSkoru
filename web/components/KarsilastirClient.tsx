"use client";

import { useNeighborhoods } from "../lib/hooks/useNeighborhoods";
import { useCompareNeighborhoods } from "../lib/hooks/useCompareNeighborhoods";
import { NeighborhoodComparisonTable } from "./NeighborhoodComparisonTable";
import { NeighborhoodMap } from "./NeighborhoodMap";
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

  const selectClass =
    "w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 sm:w-48";

  return (
    <>
      <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="flex flex-col gap-1">
          <label htmlFor="district-a" className="text-xs font-medium text-slate-500">
            Birinci ilçe
          </label>
          <select
            id="district-a"
            aria-label="Birinci ilçe"
            value={a ?? ""}
            onChange={(e) => setA(e.target.value || undefined)}
            className={selectClass}
          >
            <option value="">Seçiniz</option>
            {neighborhoods
              ?.filter((n) => n.id !== b)
              .map((n) => (
                <option key={n.id} value={n.id}>
                  {n.name}
                </option>
              ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="district-b" className="text-xs font-medium text-slate-500">
            İkinci ilçe
          </label>
          <select
            id="district-b"
            aria-label="İkinci ilçe"
            value={b ?? ""}
            onChange={(e) => setB(e.target.value || undefined)}
            className={selectClass}
          >
            <option value="">Seçiniz</option>
            {neighborhoods
              ?.filter((n) => n.id !== a)
              .map((n) => (
                <option key={n.id} value={n.id}>
                  {n.name}
                </option>
              ))}
          </select>
        </div>
      </div>

      {a && b && isPending && (
        <p role="status" className="mt-8 text-sm text-slate-500">
          Yükleniyor…
        </p>
      )}
      {isError && (
        <p className="mt-8 text-sm text-red-700">
          Karşılaştırma yüklenirken bir hata oluştu.
        </p>
      )}

      {comparison && (
        <div className="mt-8">
          <NeighborhoodComparisonTable
            nameA={nameOf(a)}
            nameB={nameOf(b)}
            scoreA={comparison.a}
            scoreB={comparison.b}
          />
        </div>
      )}

      {neighborhoods && (
        <div className="mt-8">
          <div className="mb-2 flex items-center gap-4 text-xs text-slate-500">
            <span className="flex items-center gap-1.5">
              <span className="h-2.5 w-2.5 rounded-full bg-blue-600" /> Birinci ilçe
            </span>
            <span className="flex items-center gap-1.5">
              <span className="h-2.5 w-2.5 rounded-full bg-fuchsia-600" /> İkinci ilçe
            </span>
            <span>Haritadan bir ilçeye tıklayarak da seçebilirsin.</span>
          </div>
          <div className="overflow-hidden rounded-2xl border border-slate-200 shadow-sm">
            <NeighborhoodMap
              neighborhoods={neighborhoods}
              selectedIds={[a, b]}
              onSelectDistrict={handleSelectDistrict}
            />
          </div>
        </div>
      )}
    </>
  );
}

"use client";

import { useNeighborhoods } from "../../lib/hooks/useNeighborhoods";
import { useCompareNeighborhoods } from "../../lib/hooks/useCompareNeighborhoods";
import { NeighborhoodComparisonTable } from "../../components/NeighborhoodComparisonTable";
import { NeighborhoodMap } from "../../components/NeighborhoodMap";
import { useState } from "react";

export default function KarsilastirPage() {
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

  return (
    <main className="flex min-h-screen flex-col items-center gap-8 p-12">
      <h1 className="text-2xl font-semibold text-slate-800">
        İlçe Karşılaştır
      </h1>

      <div className="flex gap-4">
        <select
          aria-label="Birinci ilçe"
          value={a ?? ""}
          onChange={(e) => setA(e.target.value || undefined)}
          className="rounded border border-slate-300 px-3 py-2"
        >
          <option value="">Seçiniz</option>
          {neighborhoods?.map((n) => (
            <option key={n.id} value={n.id}>
              {n.name}
            </option>
          ))}
        </select>
        <select
          aria-label="İkinci ilçe"
          value={b ?? ""}
          onChange={(e) => setB(e.target.value || undefined)}
          className="rounded border border-slate-300 px-3 py-2"
        >
          <option value="">Seçiniz</option>
          {neighborhoods?.map((n) => (
            <option key={n.id} value={n.id}>
              {n.name}
            </option>
          ))}
        </select>
      </div>

      {a && b && isPending && <p role="status">Yükleniyor…</p>}
      {isError && (
        <p className="text-red-700">Karşılaştırma yüklenirken bir hata oluştu.</p>
      )}

      {comparison && (
        <NeighborhoodComparisonTable
          nameA={nameOf(a)}
          nameB={nameOf(b)}
          scoreA={comparison.a}
          scoreB={comparison.b}
        />
      )}

      {neighborhoods && <NeighborhoodMap neighborhoods={neighborhoods} />}
    </main>
  );
}

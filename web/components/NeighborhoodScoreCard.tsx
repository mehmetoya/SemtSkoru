"use client";

import { useNeighborhoodScore } from "../lib/hooks/useNeighborhoodScore";
import { useNeighborhoods } from "../lib/hooks/useNeighborhoods";
import type { DimensionScore } from "../lib/types";
import { DataFreshnessBadge } from "./DataFreshnessBadge";

const DIMENSION_LABELS = {
  airQuality: "Hava Kalitesi",
  greenSpace: "Yeşil Alan",
  transportation: "Ulaşım",
} as const;

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("tr-TR", {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

function DimensionRow({
  label,
  dimension,
}: {
  label: string;
  dimension: DimensionScore;
}) {
  return (
    <div className="flex flex-col gap-1 border-b border-slate-200 py-3 last:border-0">
      <div className="flex items-center justify-between">
        <span className="font-medium text-slate-700">{label}</span>
        <span className="text-xl font-semibold text-slate-900">
          {dimension.score ?? "Veri yok"}
        </span>
      </div>
      {dimension.sourceName && dimension.publishedAt && (
        <div className="flex flex-wrap items-center gap-2 text-sm text-slate-500">
          <span>
            Kaynak: {dimension.sourceName} · {formatDate(dimension.publishedAt)}
          </span>
          <DataFreshnessBadge freshness={dimension.freshness} />
        </div>
      )}
    </div>
  );
}

export function NeighborhoodScoreCard({
  neighborhoodId,
}: {
  neighborhoodId: string;
}) {
  const { data: neighborhoods } = useNeighborhoods();
  const {
    data: score,
    isPending,
    isError,
  } = useNeighborhoodScore(neighborhoodId);

  const name =
    neighborhoods?.find((n) => n.id === neighborhoodId)?.name ??
    neighborhoodId;

  if (isPending) {
    return (
      <div className="rounded-lg border border-slate-200 p-6" role="status">
        Yükleniyor…
      </div>
    );
  }

  if (isError) {
    return (
      <div className="rounded-lg border border-red-200 p-6 text-red-700">
        Skor yüklenirken bir hata oluştu.
      </div>
    );
  }

  return (
    <div className="rounded-lg border border-slate-200 p-6">
      <div className="flex items-baseline justify-between">
        <h2 className="text-2xl font-semibold text-slate-900">{name}</h2>
        <div className="text-right">
          <div className="text-3xl font-bold text-slate-900">
            {score.overall ?? "—"}
          </div>
          <div className="text-xs text-slate-500">Genel skor</div>
        </div>
      </div>

      {!score.isComplete && (
        <p className="mt-2 text-sm text-amber-700">
          Bazı veri boyutları henüz mevcut değil.
        </p>
      )}

      <div className="mt-4">
        <DimensionRow
          label={DIMENSION_LABELS.airQuality}
          dimension={score.airQuality}
        />
        <DimensionRow
          label={DIMENSION_LABELS.greenSpace}
          dimension={score.greenSpace}
        />
        <DimensionRow
          label={DIMENSION_LABELS.transportation}
          dimension={score.transportation}
        />
      </div>
    </div>
  );
}

import Link from "next/link";
import { NeighborhoodScoreCard } from "./NeighborhoodScoreCard";
import type { AsistanOneri } from "../lib/types";

// Wraps NeighborhoodScoreCard - the SAME real dimension breakdown, ScoreBar/freshness/"Kaynak:"
// citations every other page in this app already shows - so an AI recommendation is never just
// AI prose: the numbers backing it are right there, in the exact same shape a user has already
// learned to trust elsewhere in SemtSkoru. Only the box above the card is AI-authored text.
export function AsistanOneriKart({ oneri }: { oneri: AsistanOneri }) {
  return (
    <div className="flex flex-col gap-3">
      <div className="rounded-xl border border-violet-200 bg-violet-50 px-4 py-3 dark:border-violet-900 dark:bg-violet-950/40">
        <p className="text-xs font-semibold uppercase tracking-wide text-violet-700 dark:text-violet-300">
          Neden {oneri.neighborhoodName}?
        </p>
        <p className="mt-1 text-sm text-violet-900 dark:text-violet-200">{oneri.reasoning}</p>
      </div>
      <NeighborhoodScoreCard name={oneri.neighborhoodName} score={oneri.score} headingLevel="h2" />
      <Link
        href={`/mahalle/${oneri.neighborhoodId}`}
        className="self-start text-sm font-medium text-blue-700 hover:underline dark:text-blue-400"
      >
        {oneri.neighborhoodName} sayfasına git →
      </Link>
    </div>
  );
}

"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { DistrictSearchBar, type DistrictSearchResult } from "./DistrictSearchBar";
import { NeighborhoodListCard } from "./NeighborhoodListCard";
import type { NeighborhoodSummary } from "../lib/types";

// Thin client-side wrapper around the home page's district grid (Home renders the real,
// server-fetched `neighborhoods` - see app/[locale]/page.tsx): holds only "which ids currently
// match a search", defaulting to "show everything" when no search has been run (or the last one
// didn't produce a trustworthy Ok result - see DistrictSearchBar's own comment on its `onResult`
// contract). Deliberately does NOT refetch or duplicate district data - the search endpoint only
// ever returns ids, and every district's full summary is already sitting in `neighborhoods`.
export function NeighborhoodSearchableList({ neighborhoods }: { neighborhoods: NeighborhoodSummary[] }) {
  const t = useTranslations("Search");
  const [searchResult, setSearchResult] = useState<DistrictSearchResult | null>(null);

  const visible = searchResult
    ? neighborhoods.filter((neighborhood) => searchResult.matchedIds.includes(neighborhood.id))
    : neighborhoods;
  const isFiltered = searchResult !== null;

  return (
    <div>
      <DistrictSearchBar onResult={setSearchResult} />

      {isFiltered && visible.length === 0 ? (
        <p className="mt-8 rounded-lg bg-slate-50 px-4 py-3 text-sm text-slate-600 dark:bg-slate-900 dark:text-slate-400">
          {t("noMatches")}
        </p>
      ) : (
        <>
          {isFiltered && (
            <p className="mt-8 text-sm text-slate-500 dark:text-slate-400">
              {t("resultCount", { count: visible.length })}
            </p>
          )}
          <ul className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
            {visible.map((neighborhood) => (
              <NeighborhoodListCard
                key={neighborhood.id}
                id={neighborhood.id}
                name={neighborhood.name}
                boundary={neighborhood.boundary}
                overallScore={neighborhood.overallScore}
              />
            ))}
          </ul>
        </>
      )}
    </div>
  );
}

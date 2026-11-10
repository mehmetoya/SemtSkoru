"use client";

import { useState, type FormEvent } from "react";
import { useTranslations } from "next-intl";
import { useDistrictSearch } from "../lib/hooks/useDistrictSearch";
import type { DistrictSearchResponse, DistrictSearchStatus } from "../lib/types";

const MAX_QUERY_LENGTH = 600;

// Only the 6 real dimension keys backend/.../DistrictSearchService.cs ever validates a model
// response against can end up in `dimensions` - this is a second, independent, frontend-side
// check of that same allowlist (defense in depth, same spirit as the backend's own hallucination
// guard) before using a value to look up a translation key.
const DIMENSION_KEYS = [
  "airQuality",
  "greenSpace",
  "transportation",
  "parking",
  "healthAccess",
  "transitAccess",
] as const;
type DimensionKey = (typeof DIMENSION_KEYS)[number];
function isDimensionKey(value: string): value is DimensionKey {
  return (DIMENSION_KEYS as readonly string[]).includes(value);
}

export type DistrictSearchResult = { matchedIds: string[]; dimensions: string[] };

// Mirrors AssistantClient's own status-mapping comment: the backend's `message` field is
// hardcoded Turkish and can't be localized from here, so it's never rendered directly. `status`
// is the locale-agnostic part of the contract - every value it can take is mapped to this app's
// own translated copy below (messages/{locale}.json's Search.status.*), keeping the same honest
// meaning the backend's Turkish message carries for each case without ever displaying
// backend-authored text verbatim.
const KNOWN_STATUSES: ReadonlySet<DistrictSearchStatus> = new Set([
  "InvalidRequest",
  "NotConfigured",
  "RateLimited",
  "Unavailable",
  "NoUsableCriteria",
] satisfies Exclude<DistrictSearchStatus, "Ok">[]);

export function DistrictSearchBar({
  onResult,
  onQueryChange,
  matchesDistrictName,
}: {
  // `null` means "stop filtering" - no search has been run yet, the previous one was cleared, or
  // it didn't produce a trustworthy Ok result. The parent (NeighborhoodSearchableList) falls back
  // to showing every district in that case, rather than an empty or stale-looking list - this
  // component never decides what "no filter" looks like, only when that state should apply.
  onResult: (result: DistrictSearchResult | null) => void;
  // Fired on every keystroke so the parent can filter the already-loaded district list by name
  // live, without a request - see NeighborhoodSearchableList.
  onQueryChange: (query: string) => void;
  // True while what's in the box names at least one real district. The parent has the names, this
  // component doesn't, so it's told rather than working it out. Used to keep the AI endpoint out
  // of a question the page has already answered by itself.
  matchesDistrictName: boolean;
}) {
  const t = useTranslations("Search");
  const tDimensions = useTranslations("Dimensions");
  const [query, setQuery] = useState("");
  const { mutate, data, isPending, isError, reset } = useDistrictSearch();

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    // A district name is already being matched live against the loaded list, so there is nothing
    // to ask Gemini - and asking would spend a shared, rate-limited quota only to come back with
    // "couldn't understand that", contradicting the correct results on screen.
    if (query.trim().length === 0 || isPending || matchesDistrictName) {
      return;
    }
    mutate(query, {
      onSuccess: (response: DistrictSearchResponse) => {
        onResult(
          response.status === "Ok"
            ? { matchedIds: response.matchedIds, dimensions: response.dimensions }
            : null,
        );
      },
      onError: () => onResult(null),
    });
  }

  function handleClear() {
    reset();
    setQuery("");
    onQueryChange("");
    onResult(null);
  }

  // Defensive fallback for a `status` value this frontend doesn't know about yet (e.g. the
  // backend adds a new DistrictSearchOutcomeKind before this app is updated) - still never falls
  // back to the backend's own `message` text, just the generic error copy.
  function statusMessage(status: DistrictSearchStatus): string {
    return KNOWN_STATUSES.has(status) ? t(`status.${status}`) : t("genericError");
  }

  return (
    <div className="mt-6">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3 sm:flex-row">
        <label htmlFor="district-search" className="sr-only">
          {t("label")}
        </label>
        <input
          id="district-search"
          type="text"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            onQueryChange(event.target.value);
          }}
          maxLength={MAX_QUERY_LENGTH}
          placeholder={t("placeholder")}
          className="w-full flex-1 rounded-full border border-slate-300 bg-white px-4 py-2.5 text-sm text-slate-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100"
        />
        <div className="flex shrink-0 items-center gap-3">
          <button
            type="submit"
            disabled={isPending || query.trim().length === 0 || matchesDistrictName}
            className="rounded-full bg-blue-600 px-5 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-400"
          >
            {isPending ? t("submitPending") : t("submit")}
          </button>
          {(data || isError || query.length > 0) && (
            <button
              type="button"
              onClick={handleClear}
              className="text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200"
            >
              {t("clear")}
            </button>
          )}
        </div>
      </form>

      {isPending && (
        <p role="status" className="mt-3 text-sm text-slate-500 dark:text-slate-400">
          {t("loadingStatus")}
        </p>
      )}

      {!matchesDistrictName && isError && (
        <p className="mt-3 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-300">
          {t("genericError")}
        </p>
      )}

      {!matchesDistrictName && data && data.status !== "Ok" && (
        <p className="mt-3 rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-800 dark:bg-amber-950/50 dark:text-amber-300">
          {statusMessage(data.status)}
        </p>
      )}

      {/* The search still worked and the districts below are ranked from the same real scores,
          so this is a note rather than an error - but it says plainly that the query itself was
          only keyword-matched, instead of letting a blunter result pass for the full thing. */}
      {!matchesDistrictName && data && data.status === "Ok" && data.matchedBy === "KeywordFallback" && (
        <p className="mt-3 rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-800 dark:bg-amber-950/50 dark:text-amber-300">
          {t("keywordFallbackNotice")}
        </p>
      )}

      {!matchesDistrictName && data && data.status === "Ok" && data.dimensions.length > 0 && (
        <p className="mt-3 flex flex-wrap items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400">
          <span>{t("filteringBy")}</span>
          {data.dimensions.filter(isDimensionKey).map((dimension) => (
            <span
              key={dimension}
              className="rounded-full bg-slate-100 px-2.5 py-1 font-medium text-slate-700 dark:bg-slate-800 dark:text-slate-300"
            >
              {tDimensions(dimension)}
            </span>
          ))}
        </p>
      )}
    </div>
  );
}

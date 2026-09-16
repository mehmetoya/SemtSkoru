"use client";

import { useState, type FormEvent } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useAssistant } from "../lib/hooks/useAssistant";
import { AssistantRecommendationCard } from "./AssistantRecommendationCard";
import type { AssistantStatus } from "../lib/types";

const MAX_PROMPT_LENGTH = 600;

// The backend's `message` field (backend/src/SemtSkoru.Api/Endpoints/AssistantEndpoints.cs)
// is hardcoded Turkish and can't be localized from here - so it's never rendered directly.
// `status` is the locale-agnostic part of the contract; every value it can take is mapped to
// this app's own translated copy below (messages/{locale}.json's Assistant.status.*), keeping
// the same honest meaning the backend's Turkish message carries for each case (not configured /
// rate limited / upstream down / no usable recommendation / invalid request) without ever
// displaying backend-authored text verbatim. `message` stays on the AssistantResponse type for
// now (see lib/types.ts) - just unused for display.
const KNOWN_STATUSES: ReadonlySet<AssistantStatus> = new Set([
  "InvalidRequest",
  "NotConfigured",
  "RateLimited",
  "Unavailable",
  "NoUsableRecommendations",
] satisfies Exclude<AssistantStatus, "Ok">[]);

export function AssistantClient() {
  const t = useTranslations("Assistant");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const [prompt, setPrompt] = useState("");
  const { mutate, data, isPending, isError, reset } = useAssistant(locale);

  const examplePrompts = t.raw("examples") as string[];

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (prompt.trim().length === 0 || isPending) {
      return;
    }
    mutate(prompt);
  }

  function handleClear() {
    reset();
    setPrompt("");
  }

  // Defensive fallback for a `status` value this frontend doesn't know about yet (e.g. the
  // backend adds a new AssistantOutcomeKind before this app is updated) - still never falls
  // back to the backend's own `message` text, just the generic error copy.
  function statusMessage(status: AssistantStatus): string {
    return KNOWN_STATUSES.has(status) ? t(`status.${status}`) : t("genericError");
  }

  return (
    <div className="mt-6">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <label htmlFor="assistant-prompt" className="text-sm font-medium text-slate-700 dark:text-slate-300">
          {t("promptLabel")}
        </label>
        <textarea
          id="assistant-prompt"
          value={prompt}
          onChange={(event) => setPrompt(event.target.value)}
          rows={4}
          maxLength={MAX_PROMPT_LENGTH}
          placeholder={examplePrompts[0]}
          className="w-full resize-y rounded-xl border border-slate-300 bg-white p-3 text-sm text-slate-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100"
        />
        <div className="flex flex-wrap items-center gap-3">
          <button
            type="submit"
            disabled={isPending || prompt.trim().length === 0}
            className="rounded-full bg-blue-600 px-5 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-400"
          >
            {isPending ? t("submitPending") : t("submit")}
          </button>
          {(data || isError) && (
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

      <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
        <span>{t("examplePrefix")}</span>
        {examplePrompts.map((example) => (
          <button
            key={example}
            type="button"
            onClick={() => setPrompt(example)}
            className="rounded-full bg-slate-100 px-2.5 py-1 text-left hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700"
          >
            {example}
          </button>
        ))}
      </div>

      {isPending && (
        <p role="status" className="mt-6 text-sm text-slate-500 dark:text-slate-400">
          {t("loadingStatus")}
        </p>
      )}

      {isError && (
        <p className="mt-6 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-300">
          {t("genericError")}
        </p>
      )}

      {data && data.status !== "Ok" && (
        <p className="mt-6 rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-800 dark:bg-amber-950/50 dark:text-amber-300">
          {statusMessage(data.status)}
        </p>
      )}

      {data && data.status === "Ok" && data.recommendations.length > 0 && (
        <div className="mt-8">
          <p className="mb-4 inline-flex items-center gap-1.5 rounded-full bg-violet-50 px-3 py-1.5 text-xs font-medium text-violet-800 dark:bg-violet-950/40 dark:text-violet-300">
            ✨ {tCommon("aiAttribution")}
          </p>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            {data.recommendations.map((recommendation) => (
              <AssistantRecommendationCard key={recommendation.neighborhoodId} recommendation={recommendation} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

import type { DistrictSummary } from "../lib/types";

// Same violet-tinted "AI-attributed content" visual language as AssistantClient's disclosure
// pill and AssistantRecommendationCard's "Neden X?" box, so a visitor learns to recognize
// AI-authored text in this app by its look no matter which of these three places they meet it
// first. Renders nothing at all (not an error, not a placeholder) when no summary has been
// generated yet - see DistrictSummaryGenerationJob/DistrictSummaryService for the several honest
// reasons that can be true (Gemini not configured, the weekly job hasn't reached this district
// yet, or no usable summary could be grounded in its real scores).
export function DistrictSummaryBadge({ summary }: { summary: DistrictSummary | null }) {
  if (!summary) {
    return null;
  }

  return (
    <div className="mt-4 rounded-xl border border-violet-200 bg-violet-50 px-4 py-3 dark:border-violet-900 dark:bg-violet-950/40">
      <p className="text-xs font-semibold uppercase tracking-wide text-violet-700 dark:text-violet-300">
        ✨ Öne Çıkan Özellikler
      </p>
      <p className="mt-1 text-sm text-violet-900 dark:text-violet-200">{summary.text}</p>
      <p className="mt-2 text-[11px] text-violet-500 dark:text-violet-400">
        Google Gemini ile oluşturuldu — SemtSkoru&apos;nun gerçek skor verilerine dayanır
      </p>
    </div>
  );
}

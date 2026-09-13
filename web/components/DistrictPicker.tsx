import type { NeighborhoodSummary } from "../lib/types";
import { getScoreBand, SCORE_BAND_LABELS, SCORE_BAND_STYLES } from "../lib/score-band";
import { DistrictShapeIcon } from "./DistrictShapeIcon";

// Matches NeighborhoodMap's COLOR_A/COLOR_B exactly (Tailwind's blue-600/fuchsia-600
// hex values) so a district picked here is instantly recognizable on the map below.
const ACCENT = {
  a: { border: "border-blue-200 dark:border-blue-900", ring: "focus-within:ring-blue-500", dot: "bg-blue-600" },
  b: { border: "border-fuchsia-200 dark:border-fuchsia-900", ring: "focus-within:ring-fuchsia-500", dot: "bg-fuchsia-600" },
} as const;

export function DistrictPicker({
  label,
  accent,
  neighborhoods,
  excludeId,
  value,
  onChange,
}: {
  label: string;
  accent: "a" | "b";
  neighborhoods: NeighborhoodSummary[];
  excludeId?: string;
  value?: string;
  onChange: (id: string | undefined) => void;
}) {
  const styles = ACCENT[accent];
  const selected = neighborhoods.find((n) => n.id === value);
  const band = getScoreBand(selected?.overallScore ?? null);
  const bandStyles = SCORE_BAND_STYLES[band];
  const selectId = `district-${accent}`;

  return (
    <div className={`rounded-2xl border bg-white p-4 shadow-sm dark:bg-slate-900 ${styles.border}`}>
      <div className="flex items-center gap-2">
        <span
          role="img"
          aria-label={`${label} göstergesi`}
          className={`h-2.5 w-2.5 shrink-0 rounded-full ${styles.dot}`}
        />
        <label htmlFor={selectId} className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
          {label}
        </label>
      </div>
      <select
        id={selectId}
        aria-label={label}
        value={value ?? ""}
        onChange={(e) => onChange(e.target.value || undefined)}
        className={`mt-2 w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-900 focus:outline-none focus:ring-2 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 ${styles.ring}`}
      >
        <option value="">İlçe seç…</option>
        {neighborhoods
          .filter((n) => n.id !== excludeId)
          .map((n) => (
            <option key={n.id} value={n.id}>
              {n.name}
            </option>
          ))}
      </select>

      {selected && (
        <div className="mt-3 flex items-center gap-3 border-t border-slate-100 pt-3 dark:border-slate-800">
          <DistrictShapeIcon boundary={selected.boundary} className={`h-9 w-9 shrink-0 ${bandStyles.text}`} />
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-semibold text-slate-900 dark:text-slate-100">{selected.name}</p>
            <p className={`text-xs font-medium ${bandStyles.text}`}>{SCORE_BAND_LABELS[band]}</p>
          </div>
          <div
            className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-sm font-bold ${bandStyles.bg} ${bandStyles.text}`}
          >
            {selected.overallScore ?? "—"}
          </div>
        </div>
      )}
    </div>
  );
}

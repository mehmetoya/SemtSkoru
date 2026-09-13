// A small trust signal repeated wherever the site should remind a visitor (or an AI
// crawler) that every score traces back to a real, named public data source - not a
// generic "powered by open data" claim.
export function DataSourceBadge({ className = "" }: { className?: string }) {
  return (
    <span
      className={`inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 dark:border-emerald-900/60 dark:bg-slate-900 dark:text-slate-300 ${className}`}
    >
      <svg
        width="16"
        height="16"
        viewBox="0 0 24 24"
        fill="none"
        className="shrink-0 text-emerald-600 dark:text-emerald-400"
        aria-hidden="true"
      >
        <circle cx="12" cy="12" r="2.5" fill="currentColor" />
        <path
          d="M16.24 7.76a6 6 0 0 1 0 8.48M7.76 16.24a6 6 0 0 1 0-8.48"
          stroke="currentColor"
          strokeWidth="1.8"
          strokeLinecap="round"
        />
        <path
          d="M19.07 4.93a10 10 0 0 1 0 14.14M4.93 19.07a10 10 0 0 1 0-14.14"
          stroke="currentColor"
          strokeWidth="1.8"
          strokeLinecap="round"
        />
      </svg>
      Kaynak · İBB Açık Veri
    </span>
  );
}

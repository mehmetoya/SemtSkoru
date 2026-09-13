const LOGO_PATH =
  "M13 11.2c0-2.4 2.3-3.7 3.8-3.7 2.1 0 3.5 1.1 3.9 2.6M13 11.2c0 3.1 7.7 1.7 7.7 5.2 0 1.7-1.5 3.1-3.9 3.1-1.6 0-3.5-.6-4-2.7";

/** Brand mark: a flat "S" ribbon on a solid rounded square. Kept in sync visually with
 * app/icon.svg and app/apple-icon.tsx (all three render this exact path). */
export function Logo({ className = "h-8 w-8" }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" className={className} aria-hidden="true">
      <rect width="32" height="32" rx="9" fill="#1d4ed8" />
      <path
        d={LOGO_PATH}
        fill="none"
        stroke="#ffffff"
        strokeWidth="2.4"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

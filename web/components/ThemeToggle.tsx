"use client";

import { useState } from "react";

type Theme = "light" | "dark";

function readTheme(): Theme {
  const stored = localStorage.getItem("theme");
  if (stored === "dark" || stored === "light") return stored;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function ThemeToggle() {
  // Only drives the aria-label and which way the click toggles - the icon swap itself is
  // pure CSS (`dark:` variants on the two SVGs below), already synced pre-paint by
  // ThemeScript, so there's no server/client icon tree to mismatch on hydration. (An
  // earlier version conditionally rendered one SVG or the other from this state, which
  // hydration-mismatched whenever the client's real theme differed from the server's
  // always-"light" guess - suppressHydrationWarning only covers a node's own
  // attributes/text, not a structurally different child tree.)
  const [theme, setTheme] = useState<Theme>(() =>
    typeof window === "undefined" ? "light" : readTheme(),
  );

  function toggle() {
    const next: Theme = theme === "dark" ? "light" : "dark";
    setTheme(next);
    localStorage.setItem("theme", next);
    document.documentElement.classList.toggle("dark", next === "dark");
  }

  return (
    <button
      type="button"
      onClick={toggle}
      suppressHydrationWarning
      aria-label={theme === "dark" ? "Açık temaya geç" : "Koyu temaya geç"}
      className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-slate-400 transition hover:bg-slate-100 hover:text-slate-900 dark:text-slate-500 dark:hover:bg-slate-800 dark:hover:text-slate-100"
    >
      <svg viewBox="0 0 24 24" className="hidden h-4.5 w-4.5 dark:block" fill="none" aria-hidden="true">
        <circle cx="12" cy="12" r="4.5" fill="currentColor" />
        <path
          d="M12 2.5v2M12 19.5v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M2.5 12h2M19.5 12h2M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4"
          stroke="currentColor"
          strokeWidth="1.8"
          strokeLinecap="round"
        />
      </svg>
      <svg viewBox="0 0 24 24" className="block h-4.5 w-4.5 dark:hidden" fill="currentColor" aria-hidden="true">
        <path d="M20.5 14.7A8.5 8.5 0 0 1 9.3 3.5a8.5 8.5 0 1 0 11.2 11.2Z" />
      </svg>
    </button>
  );
}

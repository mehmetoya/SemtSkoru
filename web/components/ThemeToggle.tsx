"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";

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
  const t = useTranslations("ThemeToggle");
  const [theme, setTheme] = useState<Theme>(() =>
    typeof window === "undefined" ? "light" : readTheme(),
  );

  // ThemeScript (in <head>) only ever runs once, on the browser's real initial parse of the
  // document. Switching locale re-renders the root layout (app/[locale]/layout.tsx, since it
  // reads params.locale for <html lang>) - live-verified (2026-09-16) that at some point
  // during/after that transition, Next's own DOM sync for <html> wipes the `dark` class
  // ThemeScript/toggle() had set, even though localStorage and this component's own `theme`
  // state are both untouched. Two narrower fixes were tried and empirically failed: a
  // `[theme]`-only effect (ThemeToggle isn't remounted, so it never re-fires) and a
  // `pathname`-dependent effect (fires too early - live-verified via request/timing logs that
  // Next's own swap of the freshly server-rendered <html> lands AFTER this effect runs,
  // re-wiping the class a second time). A MutationObserver sidesteps needing to win a race
  // against exactly when/how many times that swap happens: it corrects the class immediately
  // whenever the DOM disagrees with `theme`, no matter what caused the disagreement or when.
  useEffect(() => {
    const root = document.documentElement;
    const sync = () => root.classList.toggle("dark", theme === "dark");
    sync();

    const observer = new MutationObserver(sync);
    observer.observe(root, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, [theme]);

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
      aria-label={theme === "dark" ? t("toLight") : t("toDark")}
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

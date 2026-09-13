"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Logo } from "./Logo";
import { ThemeToggle } from "./ThemeToggle";

const GITHUB_URL = "https://github.com/mehmetoya/SemtSkoru";

function GitHubIcon({ className = "" }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden="true">
      <path d="M12 .5C5.73.5.5 5.73.5 12c0 5.09 3.29 9.4 7.86 10.93.58.1.79-.25.79-.56 0-.28-.01-1.02-.02-2-3.2.7-3.88-1.54-3.88-1.54-.52-1.33-1.28-1.68-1.28-1.68-1.04-.72.08-.7.08-.7 1.15.08 1.76 1.19 1.76 1.19 1.03 1.75 2.7 1.25 3.36.96.1-.75.4-1.25.73-1.54-2.55-.29-5.23-1.28-5.23-5.68 0-1.25.45-2.28 1.18-3.08-.12-.29-.51-1.46.11-3.05 0 0 .96-.31 3.15 1.18a10.9 10.9 0 0 1 5.74 0c2.19-1.49 3.15-1.18 3.15-1.18.62 1.59.23 2.76.11 3.05.74.8 1.18 1.83 1.18 3.08 0 4.41-2.69 5.38-5.25 5.67.41.36.78 1.07.78 2.15 0 1.55-.01 2.8-.01 3.18 0 .31.21.67.8.56A11.5 11.5 0 0 0 23.5 12C23.5 5.73 18.27.5 12 .5Z" />
    </svg>
  );
}

export function SiteHeader() {
  const pathname = usePathname();

  const linkClass = (href: string) =>
    pathname === href
      ? "font-semibold text-slate-900 dark:text-slate-100"
      : "text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100";

  return (
    <header className="border-b border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-x-4 gap-y-2 px-4 py-4 sm:px-6">
        <Link href="/" className="flex items-center gap-2">
          <Logo className="h-8 w-8" />
          <span className="text-lg font-bold tracking-tight text-slate-900 dark:text-slate-100">
            SemtSkoru
          </span>
        </Link>
        <nav className="flex items-center gap-3 text-sm sm:gap-5">
          <Link href="/" className={linkClass("/")} aria-current={pathname === "/" ? "page" : undefined}>
            İlçeler
          </Link>
          <Link
            href="/karsilastir"
            className={linkClass("/karsilastir")}
            aria-current={pathname === "/karsilastir" ? "page" : undefined}
          >
            Karşılaştır
          </Link>
          <Link
            href="/hakkimizda"
            className={linkClass("/hakkimizda")}
            aria-current={pathname === "/hakkimizda" ? "page" : undefined}
          >
            Hakkımızda
          </Link>
          <a
            href={GITHUB_URL}
            target="_blank"
            rel="noopener noreferrer"
            aria-label="GitHub deposu"
            className="text-slate-400 hover:text-slate-900 dark:text-slate-500 dark:hover:text-slate-100"
          >
            <GitHubIcon className="h-5 w-5" />
          </a>
          <ThemeToggle />
        </nav>
      </div>
    </header>
  );
}

"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

export function SiteHeader() {
  const pathname = usePathname();

  const linkClass = (href: string) =>
    pathname === href
      ? "font-semibold text-slate-900"
      : "text-slate-600 hover:text-slate-900";

  return (
    <header className="border-b border-slate-200 bg-white">
      <div className="mx-auto flex max-w-5xl items-center justify-between px-4 py-4 sm:px-6">
        <Link href="/" className="flex items-center gap-2">
          <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-blue-700 text-sm font-bold text-white">
            S
          </span>
          <span className="text-lg font-bold tracking-tight text-slate-900">
            SemtSkoru
          </span>
        </Link>
        <nav className="flex items-center gap-6 text-sm">
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
        </nav>
      </div>
    </header>
  );
}

import Link from "next/link";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Sayfa bulunamadı",
};

export default function NotFound() {
  return (
    <main className="mx-auto flex max-w-2xl flex-col items-center px-4 py-20 text-center sm:px-6">
      <h1 className="text-3xl font-bold tracking-tight text-slate-900 sm:text-4xl dark:text-slate-100">
        Sayfa bulunamadı
      </h1>
      <p className="mt-3 text-base text-slate-600 dark:text-slate-400">
        Aradığın sayfa ya da ilçe bulunamadı. Adres yanlış yazılmış ya da
        kaldırılmış olabilir.
      </p>
      <Link
        href="/"
        className="mt-6 font-medium text-blue-700 hover:text-blue-900 dark:text-blue-400 dark:hover:text-blue-300"
      >
        ← Tüm ilçelere dön
      </Link>
    </main>
  );
}

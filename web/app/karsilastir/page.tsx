import type { Metadata } from "next";
import { CompareClient } from "../../components/CompareClient";

export const metadata: Metadata = {
  title: "İlçe Karşılaştır",
  description:
    "İki İstanbul ilçesinin hava kalitesi, yeşil alan, ulaşım, otopark, sağlık ve toplu taşıma erişimi skorlarını yan yana ve haritada karşılaştır.",
  alternates: { canonical: "/karsilastir" },
};

export default function ComparePage() {
  return (
    <main className="mx-auto max-w-5xl px-4 py-10 sm:px-6 sm:py-14">
      <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-100">
        İlçe Karşılaştır
      </h1>
      <p className="mt-2 text-sm text-slate-600 dark:text-slate-400">
        İki ilçe seç (aşağıdan veya haritadan tıklayarak), skorlarını yan yana ve
        haritada gör.
      </p>
      <div className="mt-4 flex flex-wrap gap-2 text-xs font-medium text-slate-600 dark:text-slate-300">
        <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">39 ilçe</span>
        <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">
          Hava kalitesi verisi: 19/39 ilçe
        </span>
        <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">İBB Açık Veri Portalı</span>
      </div>
      <CompareClient />
    </main>
  );
}

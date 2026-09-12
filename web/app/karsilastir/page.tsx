import type { Metadata } from "next";
import { KarsilastirClient } from "../../components/KarsilastirClient";

export const metadata: Metadata = {
  title: "İlçe Karşılaştır",
  description:
    "İki İstanbul ilçesinin hava kalitesi, yeşil alan ve trafik skorlarını yan yana ve haritada karşılaştır.",
  alternates: { canonical: "/karsilastir" },
};

export default function KarsilastirPage() {
  return (
    <main className="mx-auto max-w-5xl px-4 py-10 sm:px-6 sm:py-14">
      <h1 className="text-3xl font-bold tracking-tight text-slate-900">
        İlçe Karşılaştır
      </h1>
      <p className="mt-2 text-sm text-slate-600">
        İki ilçe seç (aşağıdan veya haritadan tıklayarak), skorlarını yan yana ve
        haritada gör.
      </p>
      <KarsilastirClient />
    </main>
  );
}

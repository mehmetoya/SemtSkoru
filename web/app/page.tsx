import type { Metadata } from "next";
import Link from "next/link";
import { fetchNeighborhoods } from "../lib/api-client";
import { NeighborhoodListCard } from "../components/NeighborhoodListCard";
import { HighlightCard } from "../components/HighlightCard";
import { SITE_URL } from "../lib/site";
import { jsonLdScript } from "../lib/json-ld";

// Re-fetched from the API at most every 5 minutes and served from Vercel's cache the rest
// of the time - real content in the initial HTML (not "Yükleniyor…") for both search/AI
// crawlers that don't run JS, and for actual visitors who'd otherwise wait on a possibly
// cold Render instance on every page load.
export const revalidate = 300;

export const metadata: Metadata = {
  title: "SemtSkoru — İstanbul İlçe Yaşam Skoru",
  description:
    "İstanbul'un 39 ilçesi için hava kalitesi, yeşil alan, ulaşım, otopark, sağlık ve toplu taşıma erişimini gerçek İBB açık verisiyle 0-100 arası skorlara çeviren açık kaynak araç.",
  alternates: { canonical: "/" },
};

export default async function Home() {
  let neighborhoods: Awaited<ReturnType<typeof fetchNeighborhoods>> = [];
  let listError = false;
  try {
    neighborhoods = await fetchNeighborhoods();
  } catch {
    listError = true;
  }

  // Real, computed from the same data the cards below show - not a separate claim to
  // keep honest. Lets the homepage open with something more concrete than a pitch.
  const scored = neighborhoods.filter(
    (n): n is typeof n & { overallScore: number } => n.overallScore !== null,
  );
  const best = scored.length > 0 ? scored.reduce((a, b) => (b.overallScore > a.overallScore ? b : a)) : null;
  const worst = scored.length > 0 ? scored.reduce((a, b) => (b.overallScore < a.overallScore ? b : a)) : null;

  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "Dataset",
    name: "SemtSkoru İlçe Yaşam Skorları",
    description:
      "İstanbul'un 39 ilçesi için hava kalitesi, yeşil alan, ulaşım, otopark, sağlık ve toplu taşıma erişimi skorları; İBB Açık Veri Portalı ve OpenStreetMap kaynaklı.",
    url: SITE_URL,
    license: "https://data.ibb.gov.tr/pages/lisans/",
    creator: { "@type": "Organization", name: "İstanbul Büyükşehir Belediyesi Açık Veri Portalı" },
    variableMeasured: [
      "Hava Kalitesi",
      "Yeşil Alan Erişimi",
      "Ulaşım",
      "Otopark",
      "Sağlık Erişimi",
      "Toplu Taşıma Erişimi",
    ],
  };

  return (
    <main className="mx-auto max-w-6xl px-4 py-10 sm:px-6 sm:py-14">
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: jsonLdScript(jsonLd) }}
      />
      <div className="max-w-2xl">
        <h1 className="text-3xl font-bold tracking-tight text-slate-900 sm:text-4xl dark:text-slate-100">
          İstanbul&apos;u Verilerle Keşfet
        </h1>
        <p className="mt-3 text-base text-slate-600 dark:text-slate-400">
          İstanbul&apos;un 39 ilçesini hava kalitesi, yeşil alan, ulaşım, otopark,
          sağlık ve toplu taşıma erişimi açısından 0-100 arası skorlara
          çeviriyoruz — tamamı gerçek İBB Açık Veri Portalı verisiyle, her
          skorun kaynağı ve güncellik tarihiyle birlikte. Bir ilçe seç, boyut
          boyut skorlarını ve nasıl hesaplandığını gör; ya da iki ilçeyi yan
          yana ve haritada karşılaştır.
        </p>
        <div className="mt-4 flex flex-wrap gap-2 text-xs font-medium text-slate-600 dark:text-slate-300">
          <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">39 ilçe</span>
          <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">
            Hava kalitesi verisi: 19/39 ilçe
          </span>
          <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">İBB Açık Veri Portalı</span>
        </div>
      </div>

      {listError && (
        <p className="mt-8 text-sm text-red-700 dark:text-red-400">
          İlçe listesi yüklenirken bir hata oluştu.
        </p>
      )}

      {!listError && best && worst && (
        <div className="mt-8 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <HighlightCard label="En yüksek genel skor" neighborhood={best} />
          <HighlightCard label="En düşük genel skor" neighborhood={worst} />
          <Link
            href="/karsilastir"
            className="flex flex-col justify-center rounded-2xl border border-blue-200 bg-blue-50 p-5 shadow-sm transition hover:border-blue-300 hover:shadow-md dark:border-blue-900 dark:bg-blue-950/40 dark:hover:border-blue-700"
          >
            <p className="font-semibold text-blue-900 dark:text-blue-200">İki ilçeyi karşılaştır →</p>
            <p className="mt-1 text-sm text-blue-700 dark:text-blue-300">
              Skorları yan yana ve haritada gör
            </p>
          </Link>
          <Link
            href="/asistan"
            className="flex flex-col justify-center rounded-2xl border border-violet-200 bg-violet-50 p-5 shadow-sm transition hover:border-violet-300 hover:shadow-md dark:border-violet-900 dark:bg-violet-950/40 dark:hover:border-violet-700"
          >
            <p className="font-semibold text-violet-900 dark:text-violet-200">AI Semt Asistanı →</p>
            <p className="mt-1 text-sm text-violet-700 dark:text-violet-300">
              Tercihini yaz, gerçek verilere dayanan öneri al
            </p>
          </Link>
        </div>
      )}

      {!listError && (
        <ul className="mt-8 grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
          {neighborhoods.map((neighborhood) => (
            <NeighborhoodListCard
              key={neighborhood.id}
              id={neighborhood.id}
              name={neighborhood.name}
              boundary={neighborhood.boundary}
              overallScore={neighborhood.overallScore}
            />
          ))}
        </ul>
      )}
    </main>
  );
}

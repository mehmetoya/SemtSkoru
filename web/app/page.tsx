import type { Metadata } from "next";
import { fetchNeighborhoods, fetchNeighborhoodScore } from "../lib/api-client";
import { NeighborhoodListCard } from "../components/NeighborhoodListCard";
import { SITE_URL } from "../lib/site";

// Re-fetched from the API at most every 5 minutes and served from Vercel's cache the rest
// of the time - real content in the initial HTML (not "Yükleniyor…") for both search/AI
// crawlers that don't run JS, and for actual visitors who'd otherwise wait on a possibly
// cold Render instance on every page load.
export const revalidate = 300;

export const metadata: Metadata = {
  title: "SemtSkoru — İstanbul İlçe Yaşam Skoru",
  description:
    "Kadıköy, Üsküdar ve Beşiktaş için hava kalitesi, yeşil alan erişimi ve trafik yoğunluğunu gerçek İBB açık verisiyle 0-100 arası skorlara çeviren açık kaynak araç.",
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

  const scores = await Promise.all(
    neighborhoods.map((n) => fetchNeighborhoodScore(n.id).catch(() => null)),
  );

  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "Dataset",
    name: "SemtSkoru İlçe Yaşam Skorları",
    description:
      "İstanbul ilçeleri için hava kalitesi, yeşil alan erişimi ve trafik yoğunluğu skorları; İBB Açık Veri Portalı ve OpenStreetMap kaynaklı.",
    url: SITE_URL,
    license: "https://data.ibb.gov.tr/pages/lisans/",
    creator: { "@type": "Organization", name: "İstanbul Büyükşehir Belediyesi Açık Veri Portalı" },
    variableMeasured: ["Hava Kalitesi", "Yeşil Alan Erişimi", "Trafik Yoğunluğu"],
  };

  return (
    <main className="mx-auto max-w-5xl px-4 py-10 sm:px-6 sm:py-14">
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
      />
      <div className="max-w-2xl">
        <h1 className="text-3xl font-bold tracking-tight text-slate-900 sm:text-4xl">
          Nereye taşınmalısın?
        </h1>
        <p className="mt-3 text-base text-slate-600">
          Hava kalitesi, yeşil alan erişimi ve trafik yoğunluğunu gerçek İBB açık
          verisiyle 0-100 arası skorlara çeviriyoruz. Bir ilçe seç, detaylarını
          gör; veya iki ilçeyi yan yana karşılaştır.
        </p>
      </div>

      {listError && (
        <p className="mt-8 text-sm text-red-700">
          İlçe listesi yüklenirken bir hata oluştu.
        </p>
      )}

      {!listError && (
        <ul className="mt-8 grid grid-cols-1 gap-4 sm:grid-cols-3">
          {neighborhoods.map((neighborhood, i) => (
            <NeighborhoodListCard
              key={neighborhood.id}
              id={neighborhood.id}
              name={neighborhood.name}
              score={scores[i]}
            />
          ))}
        </ul>
      )}
    </main>
  );
}

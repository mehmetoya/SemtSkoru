import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { fetchNeighborhoods, fetchNeighborhoodScore } from "../../../lib/api-client";
import { NeighborhoodScoreCard } from "../../../components/NeighborhoodScoreCard";
import type { NeighborhoodScore, NeighborhoodSummary } from "../../../lib/types";
import { SITE_URL } from "../../../lib/site";

export const revalidate = 300;

async function findNeighborhood(id: string): Promise<NeighborhoodSummary | undefined> {
  const neighborhoods = await fetchNeighborhoods();
  return neighborhoods.find((n) => n.id === id);
}

export async function generateMetadata({
  params,
}: PageProps<"/mahalle/[id]">): Promise<Metadata> {
  const { id } = await params;
  const neighborhood = await findNeighborhood(id);
  if (!neighborhood) return {};

  const title = `${neighborhood.name} Yaşam Skoru`;
  const description = `${neighborhood.name} için hava kalitesi, yeşil alan erişimi ve trafik yoğunluğu skorları - gerçek İBB açık verisiyle, kaynak ve güncellik tarihiyle birlikte.`;
  return {
    title,
    description,
    alternates: { canonical: `/mahalle/${id}` },
    openGraph: { title, description, url: `${SITE_URL}/mahalle/${id}` },
  };
}

export default async function MahallePage({
  params,
}: PageProps<"/mahalle/[id]">) {
  const { id } = await params;
  const neighborhood = await findNeighborhood(id);
  if (!neighborhood) notFound();

  let score: NeighborhoodScore | null = null;
  try {
    score = await fetchNeighborhoodScore(id);
  } catch {
    score = null;
  }

  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: [
      { "@type": "ListItem", position: 1, name: "İlçeler", item: SITE_URL },
      { "@type": "ListItem", position: 2, name: neighborhood.name, item: `${SITE_URL}/mahalle/${id}` },
    ],
  };

  return (
    <main className="mx-auto max-w-2xl px-4 py-10 sm:px-6 sm:py-14">
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
      />
      <Link
        href="/"
        className="text-sm font-medium text-slate-500 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
      >
        ← Tüm ilçeler
      </Link>
      <div className="mt-4">
        {score ? (
          <NeighborhoodScoreCard
            name={neighborhood.name}
            boundary={neighborhood.boundary}
            score={score}
            share={{
              imageUrl: `/mahalle/${id}/kart`,
              fileName: `semtskoru-${id}.png`,
              shareTitle: `${neighborhood.name} Yaşam Skoru | SemtSkoru`,
              shareText: `${neighborhood.name} ilçesinin SemtSkoru yaşam skorunu incele.`,
              fallbackUrl: `${SITE_URL}/mahalle/${id}`,
            }}
          />
        ) : (
          <div className="rounded-2xl border border-red-200 bg-red-50 p-6 text-red-700 sm:p-8 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
            Skor yüklenirken bir hata oluştu.
          </div>
        )}
      </div>
    </main>
  );
}

import type { Metadata } from "next";
import { useLocale, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { Link } from "../../../../i18n/navigation";
import { fetchNeighborhoods, fetchNeighborhoodScore } from "../../../../lib/api-client";
import { NeighborhoodScoreCard } from "../../../../components/NeighborhoodScoreCard";
import type { NeighborhoodScore, NeighborhoodSummary } from "../../../../lib/types";
import { SITE_URL, localizedAlternates, localizedPath } from "../../../../lib/site";
import { jsonLdScript } from "../../../../lib/json-ld";

export const revalidate = 300;

async function findNeighborhood(id: string): Promise<NeighborhoodSummary | undefined> {
  const neighborhoods = await fetchNeighborhoods();
  return neighborhoods.find((n) => n.id === id);
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string; id: string }>;
}): Promise<Metadata> {
  const { locale, id } = await params;
  const neighborhood = await findNeighborhood(id);
  if (!neighborhood) return {};

  const t = await getTranslations({ locale, namespace: "Neighborhood" });
  const title = `${neighborhood.name} ${t("titleSuffix")}`;
  const description = t("description", { name: neighborhood.name });

  return {
    title,
    description,
    alternates: {
      canonical: localizedPath(locale, `/ilce/${id}`),
      languages: localizedAlternates(`/ilce/${id}`),
    },
    openGraph: { title, description, url: `${SITE_URL}${localizedPath(locale, `/ilce/${id}`)}` },
  };
}

// Kept async purely for data-fetching (see app/[locale]/page.tsx's Home for the same
// reasoning) - all next-intl calls happen in NeighborhoodView below instead.
export default async function NeighborhoodPage({
  params,
}: {
  params: Promise<{ locale: string; id: string }>;
}) {
  const { locale, id } = await params;

  // findNeighborhood (full district list, for the name/boundary) and the score fetch hit
  // two different backend endpoints and don't depend on each other's result - run them
  // concurrently instead of paying for both round-trips back to back. Measured live
  // (2026-09-16, real Render backend): this roughly halves this page's server-side data-
  // fetch time versus the previous sequential await/await. `locale` scopes the cached AI
  // district summary/trend embedded in the score response (see fetchNeighborhoodScore) - the
  // URL already includes [locale], so /tr/ilce/kadikoy and /en/ilce/kadikoy are already
  // separate ISR cache entries (see `revalidate` above), no extra caching work needed here.
  const [neighborhood, score] = await Promise.all([
    findNeighborhood(id),
    fetchNeighborhoodScore(id, locale).catch(() => null),
  ]);
  if (!neighborhood) notFound();

  return <NeighborhoodView id={id} neighborhood={neighborhood} score={score} />;
}

function NeighborhoodView({
  id,
  neighborhood,
  score,
}: {
  id: string;
  neighborhood: NeighborhoodSummary;
  score: NeighborhoodScore | null;
}) {
  const t = useTranslations("Neighborhood");
  const locale = useLocale();
  // Turkish (the default locale) keeps its exact, already-shared URL shape
  // (SITE_URL/ilce/{id}, no prefix); only English adds its /en prefix - matches
  // localePrefix: "as-needed" and keeps a shared link pointed at the same language its
  // sender was actually looking at.
  const localizedPageUrl = `${SITE_URL}${localizedPath(locale, `/ilce/${id}`)}`;

  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: [
      {
        "@type": "ListItem",
        position: 1,
        name: t("breadcrumbHome"),
        // Turkish keeps the exact bare SITE_URL this carried before English existed (no
        // trailing slash); only English needs its own "/en" home URL here.
        item: locale === "en" ? `${SITE_URL}/en` : SITE_URL,
      },
      { "@type": "ListItem", position: 2, name: neighborhood.name, item: localizedPageUrl },
    ],
  };

  return (
    <main className="mx-auto max-w-2xl px-4 py-10 sm:px-6 sm:py-14">
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: jsonLdScript(jsonLd) }}
      />
      <Link
        href="/"
        className="text-sm font-medium text-slate-500 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
      >
        {t("backLink")}
      </Link>
      <div className="mt-4">
        {score ? (
          <NeighborhoodScoreCard
            name={neighborhood.name}
            boundary={neighborhood.boundary}
            score={score}
            share={{
              imageUrl: `/ilce/${id}/kart`,
              fileName: `semtskoru-${id}.png`,
              shareTitle: t("shareTitle", { name: neighborhood.name }),
              shareText: t("shareText", { name: neighborhood.name }),
              fallbackUrl: localizedPageUrl,
            }}
          />
        ) : (
          <div className="rounded-2xl border border-red-200 bg-red-50 p-6 text-red-700 sm:p-8 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
            {t("errorLoadingScore")}
          </div>
        )}
      </div>
    </main>
  );
}

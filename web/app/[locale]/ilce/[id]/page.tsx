import type { Metadata } from "next";
import { useLocale, useTranslations } from "next-intl";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { notFound } from "next/navigation";
import { Link } from "../../../../i18n/navigation";
import { fetchNeighborhoods, fetchNeighborhoodScore } from "../../../../lib/api-client";
import { DistrictCarousel } from "../../../../components/DistrictCarousel";
import { NeighborhoodScoreCard } from "../../../../components/NeighborhoodScoreCard";
import type { NeighborhoodScore, NeighborhoodSummary } from "../../../../lib/types";
import { SITE_URL, localizedAlternates, localizedPath } from "../../../../lib/site";
import { jsonLdScript } from "../../../../lib/json-ld";

export const revalidate = 300;

// Required for `revalidate` above to actually do anything at runtime - NOT just documentation.
// Per node_modules/next/dist/docs/.../generate-static-params.md: "You must return an empty
// array from generateStaticParams ... in order to revalidate (ISR) paths at runtime." Omitting
// generateStaticParams entirely (as this route did before) doesn't just skip build-time
// prerendering - it makes Next.js treat every request as fully dynamic forever, with zero
// caching, no matter what `revalidate` says. Returning [] here does NOT prerender any district
// at build time (so it costs nothing against this app's shared Gemini quota - see
// DistrictSummaryGenerationJob's own remarks), it only tells Next.js this route IS eligible for
// "render once per (locale, id) on first visit, then serve the cached result for 300s" - live
// double-checked (2026-09-17) that this was the one remaining page still showing
// `x-vercel-cache: MISS` on every single request after the setRequestLocale fix elsewhere.
export function generateStaticParams() {
  return [];
}

async function findNeighborhood(id: string): Promise<NeighborhoodSummary | undefined> {
  const neighborhoods = await fetchNeighborhoods();
  return neighborhoods.find((n) => n.id === id);
}

interface NeighborhoodWithNeighbors {
  neighborhood: NeighborhoodSummary;
  previous: NeighborhoodSummary;
  next: NeighborhoodSummary;
}

// Same already-sorted-by-name array findNeighborhood above works from (fetchNeighborhoods
// mirrors the backend's `.OrderBy(n => n.Name)` - the same order the home page list and
// sitemap already use) - reused here instead of a second lookup, so the prev/next carousel
// (see DistrictCarousel and its use below) costs nothing beyond the one /api/neighborhoods call
// this page already makes. Wraps at both ends deliberately: the alphabetically-last district's
// "next" is the first one and vice versa, so the carousel feels endless instead of dead-ending -
// not a bug.
async function findNeighborhoodWithNeighbors(id: string): Promise<NeighborhoodWithNeighbors | undefined> {
  const neighborhoods = await fetchNeighborhoods();
  const index = neighborhoods.findIndex((n) => n.id === id);
  if (index === -1) return undefined;

  return {
    neighborhood: neighborhoods[index],
    previous: neighborhoods[(index - 1 + neighborhoods.length) % neighborhoods.length],
    next: neighborhoods[(index + 1) % neighborhoods.length],
  };
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
  // See app/[locale]/layout.tsx's comment on setRequestLocale: without this, NeighborhoodView's
  // useTranslations/useLocale calls below fall back to a request-header lookup that opts this
  // route out of the static rendering its `revalidate = 300` above is trying to get.
  setRequestLocale(locale);

  // findNeighborhoodWithNeighbors (full district list, for the name/boundary and the prev/next
  // carousel below) and the score fetch hit two different backend endpoints and don't depend on
  // each other's result - run them concurrently instead of paying for both round-trips back to
  // back. Measured live (2026-09-16, real Render backend): this roughly halves this page's
  // server-side data-fetch time versus the previous sequential await/await. `locale` scopes the
  // cached AI district summary/trend embedded in the score response (see
  // fetchNeighborhoodScore) - the URL already includes [locale], so /tr/ilce/kadikoy and
  // /en/ilce/kadikoy are already separate ISR cache entries (see `revalidate` above), no extra
  // caching work needed here.
  const [found, score] = await Promise.all([
    findNeighborhoodWithNeighbors(id),
    fetchNeighborhoodScore(id, locale).catch(() => null),
  ]);
  if (!found) notFound();

  return (
    <NeighborhoodView
      id={id}
      neighborhood={found.neighborhood}
      previous={found.previous}
      next={found.next}
      score={score}
    />
  );
}

function NeighborhoodView({
  id,
  neighborhood,
  previous,
  next,
  score,
}: {
  id: string;
  neighborhood: NeighborhoodSummary;
  previous: NeighborhoodSummary;
  next: NeighborhoodSummary;
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

  // No <main> wrapper here (or in a layout scoped to `[id]`) - see
  // app/[locale]/ilce/layout.tsx's comment for why that landmark has to live one segment level
  // up from this dynamic route for DistrictCarousel's slide animation to actually fire.
  return (
    <>
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
      <DistrictCarousel id={id} previous={previous} next={next}>
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
      </DistrictCarousel>
    </>
  );
}

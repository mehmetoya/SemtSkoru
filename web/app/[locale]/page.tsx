import type { Metadata } from "next";
import { useLocale, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { Link } from "../../i18n/navigation";
import { fetchNeighborhoods } from "../../lib/api-client";
import type { NeighborhoodSummary } from "../../lib/types";
import { NeighborhoodListCard } from "../../components/NeighborhoodListCard";
import { HighlightCard } from "../../components/HighlightCard";
import { SITE_URL, localizedAlternates, localizedPath } from "../../lib/site";
import { jsonLdScript } from "../../lib/json-ld";

// Re-fetched from the API at most every 5 minutes and served from Vercel's cache the rest
// of the time - real content in the initial HTML (not "Yükleniyor…") for both search/AI
// crawlers that don't run JS, and for actual visitors who'd otherwise wait on a possibly
// cold Render instance on every page load.
export const revalidate = 300;

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string }>;
}): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: "Home" });

  return {
    title: t("title"),
    description: t("description"),
    alternates: {
      canonical: localizedPath(locale, "/"),
      languages: localizedAlternates("/"),
    },
  };
}

// Kept async purely for data-fetching: intentionally has no next-intl calls of its own
// (next-intl/server's functions resolve to a "not supported" stub outside a real Next.js
// RSC request - e.g. in this component's own Vitest unit test, which calls it directly -
// see next-intl's own testing guidance to keep async Server Components free of hooks/its
// server API and delegate all translated text to a plain, non-async child component
// instead). All rendering - and every next-intl call - happens in HomeView below.
export default async function Home() {
  let neighborhoods: NeighborhoodSummary[] = [];
  let listError = false;
  try {
    neighborhoods = await fetchNeighborhoods();
  } catch {
    listError = true;
  }

  return <HomeView neighborhoods={neighborhoods} listError={listError} />;
}

function HomeView({
  neighborhoods,
  listError,
}: {
  neighborhoods: NeighborhoodSummary[];
  listError: boolean;
}) {
  const t = useTranslations("Home");
  const tCommon = useTranslations("Common");
  const tDimensions = useTranslations("Dimensions");
  const locale = useLocale();

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
    name: t("datasetJsonLdName"),
    description: t("datasetJsonLdDescription"),
    // Turkish keeps the exact bare SITE_URL this carried before English existed (no
    // trailing slash); only English needs its own "/en" home URL here.
    url: locale === "en" ? `${SITE_URL}/en` : SITE_URL,
    license: "https://data.ibb.gov.tr/pages/lisans/",
    creator: { "@type": "Organization", name: t("datasetJsonLdCreator") },
    variableMeasured: [
      tDimensions("airQuality"),
      tDimensions("greenSpace"),
      tDimensions("transportation"),
      tDimensions("parking"),
      tDimensions("healthAccess"),
      tDimensions("transitAccess"),
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
          {t("heading")}
        </h1>
        <p className="mt-3 text-base text-slate-600 dark:text-slate-400">{t("intro")}</p>
        <div className="mt-4 flex flex-wrap gap-2 text-xs font-medium text-slate-600 dark:text-slate-300">
          <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">
            {tCommon("districtCount")}
          </span>
          <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">
            {tCommon("airQualityCoverage")}
          </span>
          <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">
            {tCommon("ibbOpenData")}
          </span>
        </div>
      </div>

      {listError && (
        <p className="mt-8 text-sm text-red-700 dark:text-red-400">{t("errorLoadingList")}</p>
      )}

      {!listError && best && worst && (
        <div className="mt-8 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <HighlightCard label={t("highestScore")} neighborhood={best} />
          <HighlightCard label={t("lowestScore")} neighborhood={worst} />
          <Link
            href="/karsilastir"
            className="flex flex-col justify-center rounded-2xl border border-blue-200 bg-blue-50 p-5 shadow-sm transition hover:border-blue-300 hover:shadow-md dark:border-blue-900 dark:bg-blue-950/40 dark:hover:border-blue-700"
          >
            <p className="font-semibold text-blue-900 dark:text-blue-200">{t("compareCtaTitle")}</p>
            <p className="mt-1 text-sm text-blue-700 dark:text-blue-300">{t("compareCtaDescription")}</p>
          </Link>
          <Link
            href="/asistan"
            className="flex flex-col justify-center rounded-2xl border border-violet-200 bg-violet-50 p-5 shadow-sm transition hover:border-violet-300 hover:shadow-md dark:border-violet-900 dark:bg-violet-950/40 dark:hover:border-violet-700"
          >
            <p className="font-semibold text-violet-900 dark:text-violet-200">{t("assistantCtaTitle")}</p>
            <p className="mt-1 text-sm text-violet-700 dark:text-violet-300">{t("assistantCtaDescription")}</p>
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

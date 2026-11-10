import type { Metadata } from "next";
import { useLocale, useTranslations } from "next-intl";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { Link } from "../../../i18n/navigation";
import { DIMENSION_METHODOLOGY_KEYS } from "../../../lib/dimension-info";
import { DataSourceBadge } from "../../../components/DataSourceBadge";
import { SITE_URL, localizedAlternates, localizedPath } from "../../../lib/site";
import { jsonLdScript } from "../../../lib/json-ld";

const GITHUB_URL = "https://github.com/mehmetoya/SemtSkoru";

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string }>;
}): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: "About" });

  return {
    title: t("title"),
    description: t("description"),
    alternates: {
      canonical: localizedPath(locale, "/hakkimizda"),
      languages: localizedAlternates("/hakkimizda"),
    },
  };
}

// Kept async purely so setRequestLocale can run before AboutPageView's next-intl hooks - see
// app/[locale]/layout.tsx's comment on why that matters. Not a hook itself, so this stays safe
// to `await` directly in a test the way app/[locale]/page.tsx's Home does.
export default async function AboutPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);

  return <AboutPageView />;
}

function AboutPageView() {
  const t = useTranslations("About");
  const tDimensions = useTranslations("Dimensions");
  const locale = useLocale();

  const sourceRows = [
    ...DIMENSION_METHODOLOGY_KEYS.map((key) => ({
      key,
      label: tDimensions(key),
      source: t(`sources.${key}.source`),
      freshness: t(`sources.${key}.freshness`),
      license: t(`sources.${key}.license`),
    })),
    {
      key: "districtBoundary" as const,
      label: t("districtBoundaryLabel"),
      source: t("sources.districtBoundary.source"),
      freshness: t("sources.districtBoundary.freshness"),
      license: t("sources.districtBoundary.license"),
    },
  ];

  return (
    <main className="mx-auto max-w-3xl px-4 py-10 sm:px-6 sm:py-14">
      <h1 className="text-3xl font-bold tracking-tight text-slate-900 sm:text-4xl dark:text-slate-100">
        {t("heading")}
      </h1>

      <section className="mt-8">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{t("whyTitle")}</h2>
        <p className="mt-3 text-slate-600 dark:text-slate-400">{t("whyBody")}</p>
      </section>

      <section className="mt-10">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{t("howTitle")}</h2>
        <p className="mt-3 text-slate-600 dark:text-slate-400">{t("howIntro")}</p>
        <dl className="mt-4 space-y-4">
          {DIMENSION_METHODOLOGY_KEYS.map((key) => (
            <div key={key}>
              <dt className="font-medium text-slate-900 dark:text-slate-100">{tDimensions(key)}</dt>
              <dd className="mt-1 text-sm text-slate-600 dark:text-slate-400">
                {tDimensions(`methodology.${key}`)}
              </dd>
            </div>
          ))}
        </dl>
        <p className="mt-4 text-sm text-slate-500 dark:text-slate-400">{t("howFooter")}</p>
      </section>

      <section className="mt-10">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{t("sourcesTitle")}</h2>
        <div className="mt-4 overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
          <table className="w-full min-w-lg border-collapse text-left text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50 dark:border-slate-800 dark:bg-slate-900">
                <th className="px-4 py-2.5 font-medium text-slate-500 dark:text-slate-400">
                  {t("sourcesHeaders.dimension")}
                </th>
                <th className="px-4 py-2.5 font-medium text-slate-500 dark:text-slate-400">
                  {t("sourcesHeaders.source")}
                </th>
                <th className="px-4 py-2.5 font-medium text-slate-500 dark:text-slate-400">
                  {t("sourcesHeaders.freshness")}
                </th>
                <th className="px-4 py-2.5 font-medium text-slate-500 dark:text-slate-400">
                  {t("sourcesHeaders.license")}
                </th>
              </tr>
            </thead>
            <tbody>
              {sourceRows.map((row) => (
                <tr key={row.key} className="border-b border-slate-100 last:border-0 dark:border-slate-800">
                  <td className="px-4 py-2.5 font-medium text-slate-900 dark:text-slate-100">{row.label}</td>
                  <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">{row.source}</td>
                  <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">{row.freshness}</td>
                  <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">{row.license}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="mt-4">
          <DataSourceBadge />
        </div>
      </section>

      <section className="mt-10">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{t("openSourceTitle")}</h2>
        <p className="mt-3 text-slate-600 dark:text-slate-400">
          {t.rich("openSourceBody", {
            github: (chunks) => (
              <a
                href={GITHUB_URL}
                target="_blank"
                rel="noopener noreferrer"
                className="font-medium text-blue-700 underline hover:text-blue-900 dark:text-blue-400 dark:hover:text-blue-300"
              >
                {chunks}
              </a>
            ),
          })}
        </p>
      </section>

      <p className="mt-10 border-t border-slate-100 pt-6 text-sm text-slate-500 dark:border-slate-800 dark:text-slate-400">
        <Link href="/" className="font-medium text-blue-700 hover:text-blue-900 dark:text-blue-400 dark:hover:text-blue-300">
          {t("backLink")}
        </Link>
      </p>

      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{
          __html: jsonLdScript({
            "@context": "https://schema.org",
            "@type": "AboutPage",
            name: t("jsonLdName"),
            url: `${SITE_URL}${localizedPath(locale, "/hakkimizda")}`,
          }),
        }}
      />
    </main>
  );
}

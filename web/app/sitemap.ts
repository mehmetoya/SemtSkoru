import type { MetadataRoute } from "next";
import { fetchNeighborhoods } from "../lib/api-client";
import { SITE_URL } from "../lib/site";
import { routing } from "../i18n/routing";

// Turkish is the default, unprefixed locale (localePrefix: "as-needed" - see
// i18n/routing.ts), so its entries below are byte-for-byte the same paths this sitemap
// listed before English support was added; English adds its own `/en/...` entry alongside
// each one. Every entry also carries an `alternates.languages` map pointing at the other
// locale's version of that same page, per Google's documented sitemap-based hreflang
// pattern:
// https://developers.google.com/search/docs/specialty/international/localized-versions#sitemap
function urlFor(locale: string, path: string): string {
  const prefix = locale === routing.defaultLocale ? "" : `/${locale}`;
  return `${SITE_URL}${prefix}${path}`;
}

function entriesFor(
  path: string,
  changeFrequency: MetadataRoute.Sitemap[number]["changeFrequency"],
  priority: number,
): MetadataRoute.Sitemap {
  const languages = Object.fromEntries(
    routing.locales.map((locale) => [locale, urlFor(locale, path)]),
  );

  return routing.locales.map((locale) => ({
    url: urlFor(locale, path),
    changeFrequency,
    priority,
    alternates: { languages },
  }));
}

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const neighborhoods = await fetchNeighborhoods().catch(() => []);

  return [
    ...entriesFor("", "daily", 1),
    ...entriesFor("/karsilastir", "weekly", 0.8),
    ...entriesFor("/asistan", "monthly", 0.6),
    ...entriesFor("/hakkimizda", "monthly", 0.5),
    ...neighborhoods.flatMap((n) => entriesFor(`/mahalle/${n.id}`, "daily", 0.9)),
  ];
}

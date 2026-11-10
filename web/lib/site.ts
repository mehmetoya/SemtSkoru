export const SITE_URL =
  process.env.NEXT_PUBLIC_SITE_URL ?? "https://semtskoru.vercel.app";

// Turkish is the default, unprefixed locale (localePrefix: "as-needed" - see
// i18n/routing.ts): its canonical/hreflang paths are exactly what they were before English
// support existed. English adds its own "/en" prefix. Used for every page's
// `alternates.canonical` and `alternates.languages` so a locale's own canonical always
// points at itself, never at the other locale's URL (a wrong self-referential canonical
// would tell search engines the two languages are duplicates of each other).
export function localizedPath(locale: string, path: string): string {
  if (locale !== "en") return path;
  // Avoid an accidental "/en/" (trailing slash) for the homepage - next-intl's own
  // "as-needed" routing serves it at exactly "/en".
  return path === "/" ? "/en" : `/en${path}`;
}

export function localizedAlternates(path: string): { tr: string; en: string } {
  return { tr: localizedPath("tr", path), en: localizedPath("en", path) };
}

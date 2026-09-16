import { defineRouting } from "next-intl/routing";

// Turkish is the site's original, already-indexed default: it stays unprefixed at the
// root (semtskoru.vercel.app/karsilastir) so every existing canonical URL, sitemap entry,
// and inbound link keeps working byte-for-byte. English is additive, under /en. This is
// exactly what localePrefix: "as-needed" is for (default locale unprefixed, others
// prefixed) - see https://next-intl.dev/docs/routing#locale-prefix.
//
// Deliberately no `pathnames` config here: route segments (/asistan, /karsilastir,
// /hakkimizda, /mahalle/[id]) stay identical in both locales by design (mirrors this
// project's existing "English code identifiers, Turkish product surface" convention) -
// see AGENTS.md / the "rename Turkish code identifiers to English" commit for the same
// reasoning applied elsewhere in this codebase.
export const routing = defineRouting({
  locales: ["tr", "en"],
  defaultLocale: "tr",
  localePrefix: "as-needed",
});

export type AppLocale = (typeof routing.locales)[number];

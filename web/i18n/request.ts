import { getRequestConfig } from "next-intl/server";
import { hasLocale } from "next-intl";
import { routing } from "./routing";

// Resolves which locale's messages to load for a given request. `locale` arrives
// pre-validated whenever a caller passes one explicitly (e.g. `getTranslations({locale,
// namespace})` from a page's own `generateMetadata`); otherwise it falls back to
// next-intl's own request-locale negotiation (populated by proxy.ts via the `/en` prefix
// or, for the unprefixed default, the absence of one).
export default getRequestConfig(async ({ locale, requestLocale }) => {
  // `requestLocale` is a lazily-evaluated getter (see next-intl's getConfig): only touch
  // it - which needs a real Next.js request/proxy context - when no explicit `locale` was
  // given. This is what keeps `getTranslations({locale, namespace})` callable from plain
  // Node/Vitest contexts (unit tests, generateMetadata) without a live request.
  let resolvedLocale = locale;
  if (!resolvedLocale) {
    const requested = await requestLocale;
    resolvedLocale = hasLocale(routing.locales, requested)
      ? requested
      : routing.defaultLocale;
  }

  const messages = (await import(`../messages/${resolvedLocale}.json`)).default;

  return { locale: resolvedLocale, messages };
});

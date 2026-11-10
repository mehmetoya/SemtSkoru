import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

// Content-Security-Policy is scoped to what the app actually loads: MapLibre's tiles come
// from OpenStreetMap (fetched via XHR/fetch internally, not plain <img> tags - needs
// connect-src, not just img-src) and it runs its own logic in a blob: Web Worker; the
// district/comparison data comes from the backend API via client-side fetch, so that origin
// needs connect-src too. Everything else (scripts, styles, fonts) is self-hosted by the
// Next.js build. frame-ancestors 'none' is the actual goal here - the site has no
// login/state-changing UI, so the main real risk is a third party framing it for a
// UI-redress/brand-spoofing overlay, not clickjacking a form.
// script-src needs 'unsafe-inline': Next.js injects its own inline hydration-data scripts on
// every page, and this app has a few of its own (ThemeScript, JSON-LD) - live-verified
// (2026-09-13) that omitting this and falling back to default-src breaks the app entirely
// (Next's own hydration script gets blocked). A nonce-based script-src would tighten this
// further but needs per-request middleware; not worth the added complexity given this app
// has no user-generated content or injection surface for an inline-script CSP to guard
// against (see the project's own security audit).
const apiOrigin = process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5169";

const CSP =
  "default-src 'self'; " +
  "script-src 'self' 'unsafe-inline'; " +
  "img-src 'self' data: https://tile.openstreetmap.org; " +
  `connect-src 'self' ${apiOrigin} https://tile.openstreetmap.org; ` +
  "worker-src 'self' blob:; " +
  "style-src 'self' 'unsafe-inline'; " +
  "frame-ancestors 'none'";

const nextConfig: NextConfig = {
  poweredByHeader: false,
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Frame-Options", value: "DENY" },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          { key: "Content-Security-Policy", value: CSP },
        ],
      },
    ];
  },
  // The district route was renamed from the old MVP-era /mahalle/[id] ("mahalle" =
  // neighborhood) to /ilce/[id] ("ilce" = district), matching what the rest of the product
  // (UI copy, docs, SPEC.md) has always called these 39 İstanbul districts - "mahalle" was
  // only ever a URL leftover. The site is already live and indexed (and was just shared
  // publicly), so old /mahalle links must keep working, permanently, as 308s rather than
  // 404ing - per next/dist/docs/.../redirects.md, config-level redirects run before Proxy,
  // so this fires before next-intl's locale routing ever sees the request. Three shapes
  // cover every existing URL: the unprefixed default-locale (tr) page, the /en-prefixed
  // page (see i18n/routing.ts's localePrefix: "as-needed"), and the non-locale-prefixed
  // share-card PNG route (see proxy.ts's own comment on why /kart stays unprefixed).
  async redirects() {
    return [
      { source: "/mahalle/:id/kart", destination: "/ilce/:id/kart", permanent: true },
      { source: "/mahalle/:id", destination: "/ilce/:id", permanent: true },
      { source: "/en/mahalle/:id", destination: "/en/ilce/:id", permanent: true },
    ];
  },
};

const withNextIntl = createNextIntlPlugin("./i18n/request.ts");

export default withNextIntl(nextConfig);

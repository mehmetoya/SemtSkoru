import type { NextConfig } from "next";

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
};

export default nextConfig;

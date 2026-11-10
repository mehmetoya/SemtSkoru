// Root cause recap (see app/[locale]/layout.tsx's comment for the next-intl half of this):
// this route has a dynamic `[id]` segment with no generateStaticParams (deliberately - see
// below), so Next.js always classifies it as a "dynamic" route. Per
// node_modules/next/dist/docs/01-app/02-guides/prefetching.md, a dynamic route's <Link>
// prefetch is skipped entirely unless the route has a loading.js boundary, in which case
// Next.js prefetches "layout to first loading boundary" instead. This file is that
// boundary: it turns a click into an *instant* painted skeleton (prefetched, since it
// doesn't depend on `id` or real data) while the real score data streams in behind it,
// instead of the previous experience of the old page just sitting there inert for however
// long the Render-backend round trip took (live-measured 2026-09-17: ~1.5-2.7s TTFB, worse
// on a cold Render container - see docs/deployment.md's ~30-60s cold-start note).
//
// Deliberately NOT solved by prerendering all 39 districts via generateStaticParams
// instead: that would need calling the real backend at every Vercel build, and
// fetchNeighborhoodScore's response embeds an AI-generated summary/trend (see
// lib/api-client.ts's own comment) that can trigger a live Gemini call on a cache miss -
// prerendering 39 districts x 2 locales on every build would burn through this project's
// shared Gemini rate budget (see docs/deployment.md) for a build-time concern, not a user
// one. This file is the fix that doesn't have that cost.
//
// Loading UI components receive no props (see node_modules/next/dist/docs/.../loading.md:
// "Loading UI components do not accept any parameters") - there's no `params.locale` to read
// here, and calling next/headers' headers() ourselves to work around that would reintroduce
// the exact dynamic-rendering opt-in this file exists to avoid. So this skeleton is
// deliberately text-free/locale-neutral (purely decorative shapes) rather than risking a
// next-intl call with no cached locale to fall back on; the one bit of a11y text is hardcoded
// English, which is an intentional, narrow exception to this app's Turkish/English i18n for a
// boundary that structurally can't know the locale.
//
// A plain <div>, not <main> - this renders inside app/[locale]/ilce/layout.tsx's own <main>
// (which already carries this same spacing), so a second <main> here would be a nested landmark.
export default function NeighborhoodLoading() {
  return (
    <div role="status" aria-label="Loading">
      <div className="h-5 w-32 animate-pulse rounded bg-slate-200 dark:bg-slate-800" />

      <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8 dark:border-slate-800 dark:bg-slate-900">
        <div className="flex items-start justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="h-12 w-12 shrink-0 animate-pulse rounded-full bg-slate-200 dark:bg-slate-800" />
            <div className="h-8 w-40 animate-pulse rounded bg-slate-200 dark:bg-slate-800" />
          </div>
          <div className="h-16 w-16 shrink-0 animate-pulse rounded-full bg-slate-200 dark:bg-slate-800" />
        </div>

        <div className="mt-4 h-6 w-48 animate-pulse rounded-full bg-slate-100 dark:bg-slate-800" />

        <div className="mt-4">
          {Array.from({ length: 6 }, (_, index) => (
            <div
              key={index}
              className="border-b border-slate-100 py-4 last:border-0 dark:border-slate-800"
            >
              <div className="flex items-center justify-between gap-2">
                <div className="h-4 w-28 animate-pulse rounded bg-slate-200 dark:bg-slate-800" />
                <div className="h-2.5 w-32 animate-pulse rounded-full bg-slate-100 dark:bg-slate-800" />
              </div>
              <div className="mt-2 h-3 w-40 animate-pulse rounded bg-slate-100 dark:bg-slate-800" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

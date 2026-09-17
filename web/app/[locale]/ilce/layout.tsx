// Deliberately NOT in app/[locale]/ilce/[id]/page.tsx itself (or a layout scoped to `[id]`):
// this <main> must live in a layout scoped to the *stable* "ilce" segment, one level above the
// dynamic `[id]` segment, not to a single district's own render. `/ilce/kadikoy` ->
// `/ilce/kagithane` only re-renders from the `[id]` segment down; a layout here is shared across
// every value of `[id]` and untouched by that navigation, exactly like the root layout's <body>
// or [locale]/layout.tsx's wrapping <div> already are. Verified live: an equivalent <main>
// wrapper placed inside page.tsx (or in a layout.tsx scoped to `[id]`) sits between this route's
// own re-render and DistrictCarousel's <ViewTransition>, and silently prevented the browser's
// View Transition API from ever firing on next/previous navigation, even though the navigation
// itself still worked - see DistrictCarousel.tsx's own comment on the same finding.
export default function DistrictLayout({ children }: { children: React.ReactNode }) {
  return <main className="mx-auto max-w-2xl px-4 py-10 sm:px-6 sm:py-14">{children}</main>;
}

import { notFound } from "next/navigation";

// Next.js only renders a segment's not-found.tsx for a path it can otherwise resolve to
// that segment (e.g. a real page calling notFound()). Without this catch-all, a genuinely
// unmatched path like /en/nonexistent-page has nothing under app/[locale]/ to match at
// all, so it would skip straight to the root app/not-found.tsx (losing the translated
// version and the site header/footer). This route exists purely to give Next.js something
// to match, so it can render app/[locale]/not-found.tsx instead. Mirrors next-intl's own
// official app-router example.
export default function CatchAllPage() {
  notFound();
}

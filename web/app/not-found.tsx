import Link from "next/link";

// Renders only when Next.js can't even resolve a valid `[locale]` segment (e.g. a request
// for `/unknown.txt`, or a locale prefix that isn't "en"/nothing) - app/[locale]/layout.tsx
// calls notFound() in exactly that case, before any locale (and so any translation) is
// known, which is why this one can't be localized and stays hardcoded English. Every
// normal "page not found" case (a real, unmatched path under a valid locale, e.g.
// /mahalle/nonexistent) is handled by the translated app/[locale]/not-found.tsx instead -
// see that file, and app/[locale]/[...rest]/page.tsx, which is what makes Next.js actually
// reach it. Mirrors next-intl's own official app-router example structure. Uses next/link
// (not the locale-aware Link from i18n/navigation) deliberately - there's no known locale
// to prefix with here, so this always points at the Turkish (default) root.
export default function GlobalNotFound() {
  return (
    <html lang="en">
      <body
        style={{
          display: "flex",
          minHeight: "100vh",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          fontFamily: "system-ui, sans-serif",
          textAlign: "center",
          padding: "2rem",
        }}
      >
        <h1 style={{ fontSize: "1.75rem", fontWeight: 700 }}>Page not found</h1>
        <p style={{ marginTop: "0.75rem", color: "#64748b" }}>
          The page you requested doesn&apos;t exist.
        </p>
        <Link href="/" style={{ marginTop: "1.5rem", color: "#1d4ed8", fontWeight: 500 }}>
          ← Back to SemtSkoru
        </Link>
      </body>
    </html>
  );
}

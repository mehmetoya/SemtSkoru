import { useTranslations } from "next-intl";
import { Link } from "../../i18n/navigation";

// The real "page not found" UI for any unmatched path under a valid locale (e.g.
// /ilce/nonexistent, or a district page's own notFound() call) - rendered inside
// app/[locale]/layout.tsx, so it keeps the site's header/footer. Requires the sibling
// app/[locale]/[...rest]/page.tsx catch-all route to exist, or Next.js never reaches this
// file for an arbitrary unmatched path (see that file's comment).
export default function NotFound() {
  const t = useTranslations("NotFound");

  return (
    <main className="mx-auto flex max-w-2xl flex-col items-center px-4 py-20 text-center sm:px-6">
      <h1 className="text-3xl font-bold tracking-tight text-slate-900 sm:text-4xl dark:text-slate-100">
        {t("title")}
      </h1>
      <p className="mt-3 text-base text-slate-600 dark:text-slate-400">{t("description")}</p>
      <Link
        href="/"
        className="mt-6 font-medium text-blue-700 hover:text-blue-900 dark:text-blue-400 dark:hover:text-blue-300"
      >
        {t("backLink")}
      </Link>
    </main>
  );
}

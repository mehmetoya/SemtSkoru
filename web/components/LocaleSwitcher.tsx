"use client";

import { useTranslations } from "next-intl";
import { useParams } from "next/navigation";
import { useTransition } from "react";
import { routing } from "../i18n/routing";
import { usePathname, useRouter } from "../i18n/navigation";

// A plain two-way toggle rather than a <select> - there are only ever two locales, and
// this reads more like the rest of the header's compact pill/icon controls (next to
// ThemeToggle) than a form control would. Switches locale for the exact page the visitor
// is already on (same pathname and dynamic params, e.g. staying on the same district's
// page) rather than bouncing them back to the homepage.
export function LocaleSwitcher() {
  const t = useTranslations("LocaleSwitcher");
  const pathname = usePathname();
  const params = useParams();
  const router = useRouter();
  const [isPending, startTransition] = useTransition();

  function switchTo(locale: (typeof routing.locales)[number]) {
    // No `pathnames` config (route segments are identical in both locales - see
    // i18n/routing.ts), so `pathname` is already the literal path to switch locale on;
    // no template/params substitution needed.
    startTransition(() => {
      router.replace(pathname, { locale });
    });
  }

  return (
    <div
      role="group"
      aria-label={t("label")}
      className="flex items-center gap-0.5 rounded-lg border border-slate-200 p-0.5 text-xs font-medium dark:border-slate-700"
    >
      {routing.locales.map((locale) => {
        const isActive =
          params.locale === locale || (!params.locale && locale === routing.defaultLocale);
        return (
          <button
            key={locale}
            type="button"
            disabled={isPending}
            onClick={() => switchTo(locale)}
            aria-current={isActive}
            className={`rounded px-1.5 py-1 uppercase transition disabled:cursor-not-allowed disabled:opacity-50 ${
              isActive
                ? "bg-slate-100 text-slate-900 dark:bg-slate-800 dark:text-slate-100"
                : "text-slate-500 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
            }`}
          >
            {locale}
          </button>
        );
      })}
    </div>
  );
}

import type { Metadata } from "next";
import { useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { CompareClient } from "../../../components/CompareClient";
import { localizedAlternates, localizedPath } from "../../../lib/site";

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string }>;
}): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: "Compare" });

  return {
    title: t("title"),
    description: t("description"),
    alternates: {
      canonical: localizedPath(locale, "/karsilastir"),
      languages: localizedAlternates("/karsilastir"),
    },
  };
}

export default function ComparePage() {
  const t = useTranslations("Compare");
  const tCommon = useTranslations("Common");

  return (
    <main className="mx-auto max-w-5xl px-4 py-10 sm:px-6 sm:py-14">
      <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-100">
        {t("heading")}
      </h1>
      <p className="mt-2 text-sm text-slate-600 dark:text-slate-400">{t("intro")}</p>
      <div className="mt-4 flex flex-wrap gap-2 text-xs font-medium text-slate-600 dark:text-slate-300">
        <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">
          {tCommon("districtCount")}
        </span>
        <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">
          {tCommon("airQualityCoverage")}
        </span>
        <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">
          {tCommon("ibbOpenData")}
        </span>
      </div>
      <CompareClient />
    </main>
  );
}

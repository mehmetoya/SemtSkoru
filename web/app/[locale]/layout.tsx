import type { Metadata } from "next";
import { NextIntlClientProvider, hasLocale } from "next-intl";
import { getLocale, getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import "../globals.css";
import { Providers } from "../providers";
import { SiteHeader } from "../../components/SiteHeader";
import { SiteFooter } from "../../components/SiteFooter";
import { ThemeScript } from "../../components/ThemeScript";
import { SITE_URL } from "../../lib/site";
import { jsonLdScript } from "../../lib/json-ld";
import { routing } from "../../i18n/routing";

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string }>;
}): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: "Metadata" });

  return {
    metadataBase: new URL(SITE_URL),
    title: {
      default: t("titleDefault"),
      template: t("titleTemplate"),
    },
    description: t("description"),
    openGraph: {
      type: "website",
      locale: locale === "tr" ? "tr_TR" : "en_US",
      siteName: "SemtSkoru",
      title: t("titleDefault"),
      description: t("ogDescription"),
    },
    twitter: { card: "summary" },
  };
}

export default async function RootLayout({
  children,
  params,
}: LayoutProps<"/[locale]">) {
  const { locale } = await params;
  // A request for a path Next.js can't otherwise match (e.g. /unknown.txt) still lands
  // here with whatever it captured as `[locale]` - the root app/not-found.tsx is what
  // actually renders once this calls notFound() (see that file for why).
  if (!hasLocale(routing.locales, locale)) {
    notFound();
  }

  const resolvedLocale = await getLocale();
  const t = await getTranslations("Metadata");

  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "WebSite",
    name: "SemtSkoru",
    // Turkish keeps the exact bare SITE_URL this carried before English existed (no
    // trailing slash); only English needs its own "/en" home URL here.
    url: resolvedLocale === "en" ? `${SITE_URL}/en` : SITE_URL,
    description: t("websiteJsonLdDescription"),
    inLanguage: resolvedLocale === "tr" ? "tr-TR" : "en-US",
  };

  return (
    <html lang={resolvedLocale} suppressHydrationWarning>
      <head>
        <ThemeScript />
      </head>
      <body className="flex min-h-screen flex-col bg-slate-50 text-slate-900 antialiased dark:bg-slate-950 dark:text-slate-100">
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: jsonLdScript(jsonLd) }}
        />
        <NextIntlClientProvider>
          <Providers>
            <SiteHeader />
            <div className="flex-1">{children}</div>
            <SiteFooter />
          </Providers>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}

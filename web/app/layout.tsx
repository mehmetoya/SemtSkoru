import type { Metadata } from "next";
import "./globals.css";
import { Providers } from "./providers";
import { SiteHeader } from "../components/SiteHeader";
import { SiteFooter } from "../components/SiteFooter";
import { ThemeScript } from "../components/ThemeScript";
import { SITE_URL } from "../lib/site";

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: {
    default: "SemtSkoru — İstanbul İlçe Yaşam Skoru",
    template: "%s | SemtSkoru",
  },
  description:
    "İstanbul ilçelerinin hava kalitesi, yeşil alan erişimi ve trafik yoğunluğunu gerçek İBB açık verisiyle 0-100 arası skorlara çeviren açık kaynak araç.",
  openGraph: {
    type: "website",
    locale: "tr_TR",
    siteName: "SemtSkoru",
    title: "SemtSkoru — İstanbul İlçe Yaşam Skoru",
    description:
      "Hava kalitesi, yeşil alan erişimi ve trafik yoğunluğunu gerçek İBB açık verisiyle 0-100 arası skorlara çeviriyoruz.",
  },
  twitter: { card: "summary" },
};

const jsonLd = {
  "@context": "https://schema.org",
  "@type": "WebSite",
  name: "SemtSkoru",
  url: SITE_URL,
  description:
    "İstanbul ilçelerinin hava kalitesi, yeşil alan erişimi ve trafik yoğunluğu skorları.",
  inLanguage: "tr-TR",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="tr" suppressHydrationWarning>
      <head>
        <ThemeScript />
      </head>
      <body className="flex min-h-screen flex-col bg-slate-50 text-slate-900 antialiased dark:bg-slate-950 dark:text-slate-100">
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
        />
        <Providers>
          <SiteHeader />
          <div className="flex-1">{children}</div>
          <SiteFooter />
        </Providers>
      </body>
    </html>
  );
}

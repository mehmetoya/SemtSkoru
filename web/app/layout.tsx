import type { Metadata } from "next";
import "./globals.css";
import { Providers } from "./providers";
import { SiteHeader } from "../components/SiteHeader";
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
    <html lang="tr">
      <body className="min-h-screen bg-slate-50 text-slate-900 antialiased">
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
        />
        <Providers>
          <SiteHeader />
          {children}
        </Providers>
      </body>
    </html>
  );
}

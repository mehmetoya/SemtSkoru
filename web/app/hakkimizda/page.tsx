import type { Metadata } from "next";
import Link from "next/link";
import { DIMENSION_METHODOLOGY } from "../../lib/dimension-info";
import { DataSourceBadge } from "../../components/DataSourceBadge";
import { SITE_URL } from "../../lib/site";

export const metadata: Metadata = {
  title: "Hakkımızda",
  description:
    "SemtSkoru neden var, skorları nasıl hesaplıyoruz ve hangi verilere dayanıyoruz - kaynak, lisans ve güncellik bilgisiyle birlikte.",
  alternates: { canonical: "/hakkimizda" },
};

const SOURCES = [
  {
    label: "Hava Kalitesi",
    source: "İBB Açık Veri Portalı (canlı API)",
    freshness: "Saatlik",
    license: "İBB Açık Veri Lisansı",
  },
  {
    label: "Yeşil Alan",
    source: "İBB Açık Veri Portalı (GeoJSON)",
    freshness: "Yıllık",
    license: "İBB Açık Veri Lisansı",
  },
  {
    label: "Trafik / Ulaşım",
    source: "İBB Açık Veri Portalı (Ocak 2025 CSV)",
    freshness: "Tarihsel — canlı değil, UI'da açıkça etiketli",
    license: "İBB Açık Veri Lisansı",
  },
  {
    label: "İlçe sınırları",
    source: "OpenStreetMap / Nominatim",
    freshness: "Statik (bir kez çekildi)",
    license: "ODbL",
  },
];

export default function HakkimizdaPage() {
  return (
    <main className="mx-auto max-w-3xl px-4 py-10 sm:px-6 sm:py-14">
      <h1 className="text-3xl font-bold tracking-tight text-slate-900 sm:text-4xl dark:text-slate-100">
        Hakkımızda
      </h1>

      <section className="mt-8">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">Neden SemtSkoru?</h2>
        <p className="mt-3 text-slate-600 dark:text-slate-400">
          İstanbul için birçok açık veri projesi veriyi haritada gösterir, ama bir
          karara dönüştürmez. SemtSkoru &quot;Nereye taşınmalıyım, hangi ilçe daha
          uygun?&quot; sorusuna doğrudan cevap vermeyi hedefler: hava kalitesi,
          yeşil alan erişimi ve trafik yoğunluğunu tek bir 0-100 skora indirip iki
          ilçeyi doğrudan karşılaştırmanı sağlar.
        </p>
      </section>

      <section className="mt-10">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">Nasıl skorluyoruz?</h2>
        <p className="mt-3 text-slate-600 dark:text-slate-400">
          Her boyut, ilçenin ham veriden doğrusal bir formülle 0-100&apos;e taşınan
          gerçek bir ölçümüdür — tahmin ya da öznel bir değerlendirme değil:
        </p>
        <dl className="mt-4 space-y-4">
          <div>
            <dt className="font-medium text-slate-900 dark:text-slate-100">Hava Kalitesi</dt>
            <dd className="mt-1 text-sm text-slate-600 dark:text-slate-400">{DIMENSION_METHODOLOGY.airQuality}</dd>
          </div>
          <div>
            <dt className="font-medium text-slate-900 dark:text-slate-100">Yeşil Alan</dt>
            <dd className="mt-1 text-sm text-slate-600 dark:text-slate-400">{DIMENSION_METHODOLOGY.greenSpace}</dd>
          </div>
          <div>
            <dt className="font-medium text-slate-900 dark:text-slate-100">Ulaşım</dt>
            <dd className="mt-1 text-sm text-slate-600 dark:text-slate-400">{DIMENSION_METHODOLOGY.transportation}</dd>
          </div>
        </dl>
        <p className="mt-4 text-sm text-slate-500 dark:text-slate-400">
          Genel skor, veri bulunan boyutların basit ortalamasıdır. Bir ilçe için
          bir boyutta veri yoksa (örneğin hava kalitesi istasyonu olmayan
          ilçelerde), o boyut ortalamaya dahil edilmez ve arayüzde açıkça
          &quot;Veri yok&quot; olarak gösterilir — hiçbir zaman tahmin veya
          enterpolasyonla doldurulmaz.
        </p>
      </section>

      <section className="mt-10">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">Veri kaynakları</h2>
        <div className="mt-4 overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
          <table className="w-full min-w-lg border-collapse text-left text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50 dark:border-slate-800 dark:bg-slate-900">
                <th className="px-4 py-2.5 font-medium text-slate-500 dark:text-slate-400">Boyut</th>
                <th className="px-4 py-2.5 font-medium text-slate-500 dark:text-slate-400">Kaynak</th>
                <th className="px-4 py-2.5 font-medium text-slate-500 dark:text-slate-400">Güncellik</th>
                <th className="px-4 py-2.5 font-medium text-slate-500 dark:text-slate-400">Lisans</th>
              </tr>
            </thead>
            <tbody>
              {SOURCES.map((row) => (
                <tr key={row.label} className="border-b border-slate-100 last:border-0 dark:border-slate-800">
                  <td className="px-4 py-2.5 font-medium text-slate-900 dark:text-slate-100">{row.label}</td>
                  <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">{row.source}</td>
                  <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">{row.freshness}</td>
                  <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">{row.license}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="mt-4">
          <DataSourceBadge />
        </div>
      </section>

      <section className="mt-10">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">Açık kaynak</h2>
        <p className="mt-3 text-slate-600 dark:text-slate-400">
          SemtSkoru MIT lisansıyla tamamen açık kaynaktır — kodun tamamı, skor
          formülleri dahil,{" "}
          <a
            href="https://github.com/mehmetoya/SemtSkoru"
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-blue-700 underline hover:text-blue-900 dark:text-blue-400 dark:hover:text-blue-300"
          >
            GitHub&apos;da
          </a>{" "}
          herkese açık. Bir hata bulursan veya bir veri kaynağı değişirse, katkı
          sağlayabilirsin.
        </p>
      </section>

      <p className="mt-10 border-t border-slate-100 pt-6 text-sm text-slate-500 dark:border-slate-800 dark:text-slate-400">
        <Link href="/" className="font-medium text-blue-700 hover:text-blue-900 dark:text-blue-400 dark:hover:text-blue-300">
          ← Tüm ilçelere dön
        </Link>
      </p>

      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{
          __html: JSON.stringify({
            "@context": "https://schema.org",
            "@type": "AboutPage",
            name: "Hakkımızda",
            url: `${SITE_URL}/hakkimizda`,
          }),
        }}
      />
    </main>
  );
}

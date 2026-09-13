import Link from "next/link";
import { DataSourceBadge } from "./DataSourceBadge";
import { Logo } from "./Logo";

const GITHUB_URL = "https://github.com/mehmetoya/SemtSkoru";

function GitHubIcon({ className = "" }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden="true">
      <path d="M12 .5C5.73.5.5 5.73.5 12c0 5.09 3.29 9.4 7.86 10.93.58.1.79-.25.79-.56 0-.28-.01-1.02-.02-2-3.2.7-3.88-1.54-3.88-1.54-.52-1.33-1.28-1.68-1.28-1.68-1.04-.72.08-.7.08-.7 1.15.08 1.76 1.19 1.76 1.19 1.03 1.75 2.7 1.25 3.36.96.1-.75.4-1.25.73-1.54-2.55-.29-5.23-1.28-5.23-5.68 0-1.25.45-2.28 1.18-3.08-.12-.29-.51-1.46.11-3.05 0 0 .96-.31 3.15 1.18a10.9 10.9 0 0 1 5.74 0c2.19-1.49 3.15-1.18 3.15-1.18.62 1.59.23 2.76.11 3.05.74.8 1.18 1.83 1.18 3.08 0 4.41-2.69 5.38-5.25 5.67.41.36.78 1.07.78 2.15 0 1.55-.01 2.8-.01 3.18 0 .31.21.67.8.56A11.5 11.5 0 0 0 23.5 12C23.5 5.73 18.27.5 12 .5Z" />
    </svg>
  );
}

export function SiteFooter() {
  return (
    <footer className="mt-16 border-t border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <div className="flex flex-col gap-8 sm:flex-row sm:justify-between">
          <div className="max-w-sm">
            <Link href="/" className="flex items-center gap-2">
              <Logo className="h-7 w-7" />
              <span className="text-base font-bold tracking-tight text-slate-900 dark:text-slate-100">
                SemtSkoru
              </span>
            </Link>
            <p className="mt-3 text-sm text-slate-500 dark:text-slate-400">
              İstanbul ilçelerinin hava kalitesi, yeşil alan erişimi ve trafik
              yoğunluğunu gerçek İBB açık verisiyle 0-100 arası skorlara çeviren
              açık kaynak bir araç.
            </p>
            <DataSourceBadge className="mt-4" />
          </div>

          <div className="grid grid-cols-2 gap-8 text-sm sm:gap-16">
            <div>
              <p className="font-semibold text-slate-900 dark:text-slate-100">Ürün</p>
              <ul className="mt-3 space-y-2 text-slate-500 dark:text-slate-400">
                <li>
                  <Link href="/" className="hover:text-slate-900 dark:hover:text-slate-100">
                    İlçeler
                  </Link>
                </li>
                <li>
                  <Link href="/karsilastir" className="hover:text-slate-900 dark:hover:text-slate-100">
                    Karşılaştır
                  </Link>
                </li>
                <li>
                  <Link href="/hakkimizda" className="hover:text-slate-900 dark:hover:text-slate-100">
                    Hakkımızda
                  </Link>
                </li>
              </ul>
            </div>
            <div>
              <p className="font-semibold text-slate-900 dark:text-slate-100">Proje</p>
              <ul className="mt-3 space-y-2 text-slate-500 dark:text-slate-400">
                <li>
                  <a
                    href={GITHUB_URL}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex items-center gap-1.5 hover:text-slate-900 dark:hover:text-slate-100"
                  >
                    <GitHubIcon className="h-4 w-4" /> GitHub
                  </a>
                </li>
                <li>
                  <a
                    href={`${GITHUB_URL}/blob/main/LICENSE`}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="hover:text-slate-900 dark:hover:text-slate-100"
                  >
                    MIT Lisansı
                  </a>
                </li>
              </ul>
            </div>
          </div>
        </div>

        <div className="mt-10 border-t border-slate-100 pt-6 text-xs text-slate-400 dark:border-slate-800 dark:text-slate-500">
          <p>
            © {new Date().getFullYear()} SemtSkoru · Veriler{" "}
            <a
              href="https://data.ibb.gov.tr"
              target="_blank"
              rel="noopener noreferrer"
              className="underline hover:text-slate-600 dark:hover:text-slate-300"
            >
              İBB Açık Veri Portalı
            </a>{" "}
            (İBB Açık Veri Lisansı) ve{" "}
            <a
              href="https://www.openstreetmap.org/copyright"
              target="_blank"
              rel="noopener noreferrer"
              className="underline hover:text-slate-600 dark:hover:text-slate-300"
            >
              OpenStreetMap katkıda bulunanları
            </a>{" "}
            (ODbL) kaynaklıdır.
          </p>
        </div>
      </div>
    </footer>
  );
}

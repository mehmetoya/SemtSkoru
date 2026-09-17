"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";

function DownloadIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
    >
      <path d="M12 3v12m0 0-4-4m4 4 4-4M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2" />
    </svg>
  );
}

function ShareIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
    >
      <path d="M12 16V4m0 0-4 4m4-4 4 4M4 16v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3" />
    </svg>
  );
}

// A single subdued "ghost" style for both actions - sharing/downloading is secondary to
// whatever card it sits on (the score or the comparison), so neither button should carry
// the site's primary-CTA blue fill, which would visually outrank the actual content.
const GHOST_BUTTON =
  "inline-flex h-9 shrink-0 items-center gap-1.5 rounded-full border border-slate-200 px-3.5 text-xs font-medium text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 hover:text-slate-900 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:text-slate-300 dark:hover:border-slate-600 dark:hover:bg-slate-800 dark:hover:text-slate-100 dark:focus:ring-offset-slate-950";

// Shared by the single-district score card (app/[locale]/ilce/[id]/page.tsx) and the
// comparison result (CompareClient.tsx) - both point this at their own
// ImageResponse route (app/ilce/[id]/kart or app/karsilastir/kart), which renders a
// fixed light-mode PNG, timestamped at generation time, honoring "Veri yok" for any
// missing dimension. Downloading needs no JS at all (a plain `download` anchor); sharing
// needs the Web Share API, with a clipboard-copy fallback for browsers without it.
export function ShareCardButtons({
  imageUrl,
  fileName,
  shareTitle,
  shareText,
  fallbackUrl,
}: {
  imageUrl: string;
  fileName: string;
  shareTitle: string;
  shareText: string;
  // Copied to the clipboard when the browser can't share (or can't share files) -
  // callers choose whether that's the live page (still current when opened later) or
  // the image URL itself (a stable, frozen snapshot - see ShareCardButtons callers).
  fallbackUrl: string;
}) {
  const t = useTranslations("ShareCardButtons");
  const [isSharing, setIsSharing] = useState(false);
  const [status, setStatus] = useState<string | null>(null);

  async function handleShare() {
    setStatus(null);

    if (typeof navigator === "undefined" || !navigator.share) {
      await copyFallbackLink();
      return;
    }

    setIsSharing(true);
    try {
      const response = await fetch(imageUrl);
      if (!response.ok) throw new Error(t("imageDownloadFailed"));
      const blob = await response.blob();
      const file = new File([blob], fileName, { type: "image/png" });

      if (navigator.canShare?.({ files: [file] })) {
        await navigator.share({ title: shareTitle, text: shareText, files: [file] });
      } else {
        await navigator.share({ title: shareTitle, text: shareText, url: fallbackUrl });
      }
    } catch (error) {
      // AbortError just means the user closed the native share sheet - not a failure.
      if (error instanceof Error && error.name === "AbortError") {
        setStatus(null);
      } else {
        setStatus(t("shareFailed"));
      }
    } finally {
      setIsSharing(false);
    }
  }

  async function copyFallbackLink() {
    try {
      await navigator.clipboard.writeText(fallbackUrl);
      setStatus(t("linkCopied"));
    } catch {
      setStatus(t("shareNotSupported", { url: fallbackUrl }));
    }
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      <a href={imageUrl} download={fileName} className={GHOST_BUTTON}>
        <DownloadIcon className="h-3.5 w-3.5" />
        {t("download")}
      </a>
      <button type="button" onClick={handleShare} disabled={isSharing} className={GHOST_BUTTON}>
        <ShareIcon className="h-3.5 w-3.5" />
        {isSharing ? t("sharing") : t("share")}
      </button>
      <p role="status" aria-live="polite" className="w-full text-xs text-slate-500 dark:text-slate-400 empty:hidden">
        {status}
      </p>
    </div>
  );
}

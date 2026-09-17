"use client";

import { type ReactNode, type TouchEvent as ReactTouchEvent, useEffect, useRef } from "react";
import { ViewTransition } from "react";
import { useTranslations } from "next-intl";
import { Link } from "../i18n/navigation";

interface DistrictLink {
  id: string;
  name: string;
}

interface TouchState {
  x: number;
  y: number;
  t: number;
}

// A short horizontal wobble while someone is scrolling the page vertically must never trigger a
// navigation. SWIPE_DISTANCE_PX is the minimum horizontal travel required on its own;
// SWIPE_FLICK_PX/SWIPE_FLICK_VELOCITY is the alternate "fast flick" path (less distance, but
// fast), so a short, quick swipe still registers. DIRECTION_RATIO is how much further
// horizontal must travel than vertical before this counts as a swipe at all - below that, it's
// ordinary vertical scrolling and nothing happens.
const SWIPE_DISTANCE_PX = 50;
const SWIPE_FLICK_PX = 24;
const SWIPE_FLICK_VELOCITY_PX_MS = 0.5;
const SWIPE_DIRECTION_RATIO = 1.5;

// The district detail page's previous/next carousel (see app/[locale]/ilce/[id]/page.tsx).
// `previous`/`next` are real pages - each <Link> below is a genuine navigation (its own URL,
// its own ISR cache entry, real prefetching), not a client-only fake carousel. Swipe and the
// arrow-key shortcut both drive the exact same two <Link>s (via `.click()`) rather than calling
// `router.push` themselves, so there is only one place - these links' `href`/`transitionTypes` -
// that decides where a gesture goes and how its slide animates.
export function DistrictCarousel({
  id,
  previous,
  next,
  children,
}: {
  // The district currently being viewed - every /ilce/[id] navigation reuses this exact same
  // page template, so without a `key` tied to it, React treats kadikoy -> kagithane as a prop
  // update to the *same* <ViewTransition> instance rather than an unmount/mount pair, and
  // per node_modules/next/dist/docs/.../view-transitions.md's "Crossfade content within the
  // same route" step, `enter`/`exit` (unlike `share`) only fire for an actual mount/unmount -
  // an in-place update animates nothing at all. Verified live: without this `key`, clicking
  // next/previous still navigated correctly but never invoked the browser's View Transition API.
  id: string;
  previous: DistrictLink;
  next: DistrictLink;
  children: ReactNode;
}) {
  const t = useTranslations("Neighborhood");
  const previousLinkRef = useRef<HTMLAnchorElement>(null);
  const nextLinkRef = useRef<HTMLAnchorElement>(null);
  const touchStart = useRef<TouchState | null>(null);

  // Left/right arrow keys are a bonus on top of the required click/tap controls. Ignored while
  // modifier keys are held (Alt/Cmd+Left is browser back/forward in several setups) or while
  // focus is in an editable field - neither exists on this page today, but the guard is cheap
  // insurance against this handler ever fighting a future one.
  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
        return;
      }
      const target = event.target as HTMLElement | null;
      if (target && (/^(input|textarea|select)$/i.test(target.tagName) || target.isContentEditable)) {
        return;
      }

      if (event.key === "ArrowLeft") {
        previousLinkRef.current?.click();
      } else if (event.key === "ArrowRight") {
        nextLinkRef.current?.click();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  function handleTouchStart(event: ReactTouchEvent<HTMLDivElement>) {
    if (event.touches.length !== 1) {
      touchStart.current = null;
      return;
    }
    const touch = event.touches[0];
    touchStart.current = { x: touch.clientX, y: touch.clientY, t: event.timeStamp };
  }

  function handleTouchEnd(event: ReactTouchEvent<HTMLDivElement>) {
    const start = touchStart.current;
    touchStart.current = null;
    if (!start || event.changedTouches.length === 0) return;

    const touch = event.changedTouches[0];
    const dx = touch.clientX - start.x;
    const dy = touch.clientY - start.y;
    // Predominantly vertical: this is a scroll, not a swipe.
    if (Math.abs(dx) < Math.abs(dy) * SWIPE_DIRECTION_RATIO) return;

    const elapsedMs = Math.max(event.timeStamp - start.t, 1);
    const velocity = Math.abs(dx) / elapsedMs;
    const travelledFarEnough = Math.abs(dx) >= SWIPE_DISTANCE_PX;
    const flicked = Math.abs(dx) >= SWIPE_FLICK_PX && velocity >= SWIPE_FLICK_VELOCITY_PX_MS;
    if (!travelledFarEnough && !flicked) return;

    // Swipe left reveals "next" (content moves left, like turning a page forward); swipe right
    // goes to "previous" - the same left/right convention as the click/arrow-key navigation and
    // the slide animation direction (see globals.css's .district-forward/.district-back rules).
    if (dx < 0) {
      nextLinkRef.current?.click();
    } else {
      previousLinkRef.current?.click();
    }
  }

  return (
    // No wrapping host element around the whole thing (a plain <div> here would sit between
    // this route's own render and <ViewTransition> below) - and the nav row deliberately isn't
    // inside <ViewTransition> either, since named participants stop being real hit-test targets
    // for the transition's duration (see the guide's "Keeping the page interactive" section) and
    // this is exactly where someone flipping through districts is most likely to click again
    // right away. Verified live: with a wrapping <div> here (any wrapping <div>, anywhere between
    // this route's page.tsx and <ViewTransition>), clicking next/previous still navigated
    // correctly but never invoked the browser's View Transition API at all.
    <>
      <nav
        aria-label={t("districtNavLabel")}
        className="mt-4 mb-3 flex items-center justify-between gap-3"
      >
        <Link
          ref={previousLinkRef}
          href={`/ilce/${previous.id}`}
          transitionTypes={["district-back"]}
          aria-label={t("previousDistrict", { name: previous.name })}
          className="group inline-flex min-w-0 items-center gap-1.5 rounded-full border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:border-slate-300 hover:text-slate-900 dark:border-slate-800 dark:text-slate-300 dark:hover:border-slate-700 dark:hover:text-slate-100"
        >
          <ChevronIcon direction="left" className="h-4 w-4 shrink-0" />
          <span className="truncate">{previous.name}</span>
        </Link>
        <Link
          ref={nextLinkRef}
          href={`/ilce/${next.id}`}
          transitionTypes={["district-forward"]}
          aria-label={t("nextDistrict", { name: next.name })}
          className="group inline-flex min-w-0 items-center gap-1.5 rounded-full border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:border-slate-300 hover:text-slate-900 dark:border-slate-800 dark:text-slate-300 dark:hover:border-slate-700 dark:hover:text-slate-100"
        >
          <span className="truncate">{next.name}</span>
          <ChevronIcon direction="right" className="h-4 w-4 shrink-0" />
        </Link>
      </nav>

      <ViewTransition
        key={id}
        enter={{ "district-forward": "district-forward", "district-back": "district-back", default: "none" }}
        exit={{ "district-forward": "district-forward", "district-back": "district-back", default: "none" }}
        default="none"
      >
        {/* touchAction: "pan-y" hands vertical drags to the browser's native scrolling untouched
            and refuses to let it treat a horizontal drag as a scroll gesture either - so the
            handlers above only ever have to classify a gesture, never fight one already in
            progress or call preventDefault from a (React-passive-by-default) touch listener.
            This div is INSIDE <ViewTransition>, not wrapping it - see the note above. */}
        <div
          data-testid="district-carousel-swipe-area"
          style={{ touchAction: "pan-y" }}
          onTouchStart={handleTouchStart}
          onTouchEnd={handleTouchEnd}
          onTouchCancel={() => {
            touchStart.current = null;
          }}
        >
          {children}
        </div>
      </ViewTransition>
    </>
  );
}

function ChevronIcon({ direction, className = "" }: { direction: "left" | "right"; className?: string }) {
  return (
    <svg viewBox="0 0 20 20" fill="none" className={className} aria-hidden="true">
      <path
        d={direction === "left" ? "M12.5 15 7.5 10l5-5" : "M7.5 15 12.5 10l-5-5"}
        stroke="currentColor"
        strokeWidth={1.75}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

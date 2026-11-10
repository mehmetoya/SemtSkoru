import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { createIntlWrapper } from "../../lib/test-utils";
import { DistrictCarousel } from "../DistrictCarousel";

// React's <ViewTransition> ships on the canary React build Next.js aliases 'react' to for App
// Router code (see node_modules/next/dist/docs/.../view-transitions.md: "You do not need to
// install react@canary yourself") - that aliasing is a Next.js build-time mechanism, so it
// doesn't apply here under Vitest/Vite, which resolves 'react' to the plain stable package this
// repo's devDependencies installed, and that package doesn't export it yet. This mirrors the
// doc's own stated graceful-degradation behavior ("Without browser support ... the transitions
// do not animate") by swapping in a plain passthrough - DistrictCarousel's real navigation and
// gesture logic (the actual thing these tests exercise) is unaffected either way.
vi.mock("react", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react")>();
  return {
    ...actual,
    ViewTransition: ({ children }: { children?: ReactNode }) => children,
  };
});

const previous = { id: "besiktas", name: "Beşiktaş" };
const next = { id: "uskudar", name: "Üsküdar" };

describe("DistrictCarousel", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders real links to the previous/next districts with translated aria-labels, plus the current district's content", () => {
    render(
      <DistrictCarousel id="kadikoy" previous={previous} next={next}>
        <p>Kadıköy skor kartı</p>
      </DistrictCarousel>,
      { wrapper: createIntlWrapper() },
    );

    const previousLink = screen.getByRole("link", { name: "Önceki ilçe: Beşiktaş" });
    expect(previousLink).toHaveAttribute("href", "/ilce/besiktas");
    expect(previousLink).toHaveTextContent("Beşiktaş");

    const nextLink = screen.getByRole("link", { name: "Sonraki ilçe: Üsküdar" });
    expect(nextLink).toHaveAttribute("href", "/ilce/uskudar");
    expect(nextLink).toHaveTextContent("Üsküdar");

    expect(screen.getByRole("navigation", { name: "İlçeler arasında gezin" })).toBeInTheDocument();
    expect(screen.getByText("Kadıköy skor kartı")).toBeInTheDocument();
  });

  it("clicks the next/previous link on ArrowRight/ArrowLeft, ignoring modified key presses", () => {
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    render(
      <DistrictCarousel id="kadikoy" previous={previous} next={next}>
        <p>content</p>
      </DistrictCarousel>,
      { wrapper: createIntlWrapper() },
    );

    // A modifier held down (e.g. Alt+Left/Cmd+Left is browser back/forward in several setups)
    // must not trigger our navigation.
    fireEvent.keyDown(window, { key: "ArrowRight", altKey: true });
    expect(clickSpy).not.toHaveBeenCalled();

    fireEvent.keyDown(window, { key: "ArrowRight" });
    expect(clickSpy).toHaveBeenCalledTimes(1);

    fireEvent.keyDown(window, { key: "ArrowLeft" });
    expect(clickSpy).toHaveBeenCalledTimes(2);
  });

  it("navigates on a deliberate horizontal swipe but ignores a predominantly vertical gesture (page scroll)", () => {
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    render(
      <DistrictCarousel id="kadikoy" previous={previous} next={next}>
        <p>content</p>
      </DistrictCarousel>,
      { wrapper: createIntlWrapper() },
    );

    const swipeArea = screen.getByTestId("district-carousel-swipe-area");

    // Mostly vertical movement: a scroll, not a swipe - must not navigate.
    fireEvent.touchStart(swipeArea, { touches: [{ clientX: 100, clientY: 100 }] });
    fireEvent.touchEnd(swipeArea, { changedTouches: [{ clientX: 90, clientY: 240 }] });
    expect(clickSpy).not.toHaveBeenCalled();

    // Swipe left far enough -> next.
    fireEvent.touchStart(swipeArea, { touches: [{ clientX: 200, clientY: 100 }] });
    fireEvent.touchEnd(swipeArea, { changedTouches: [{ clientX: 120, clientY: 105 }] });
    expect(clickSpy).toHaveBeenCalledTimes(1);

    // Swipe right far enough -> previous.
    fireEvent.touchStart(swipeArea, { touches: [{ clientX: 100, clientY: 100 }] });
    fireEvent.touchEnd(swipeArea, { changedTouches: [{ clientX: 180, clientY: 98 }] });
    expect(clickSpy).toHaveBeenCalledTimes(2);
  });
});

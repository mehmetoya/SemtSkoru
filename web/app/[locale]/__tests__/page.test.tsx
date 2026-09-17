import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import Home from "../page";
import { createQueryWrapper, jsonResponse, SAMPLE_BOUNDARY } from "../../../lib/test-utils";
import type { NeighborhoodSummary } from "../../../lib/types";

// Vitest's jsdom environment makes next-intl resolve "next-intl/server" to its
// react-client build (there's no real RSC renderer here), where every export - including
// setRequestLocale - is a stub that throws "not supported in Client Components" (see
// node_modules/next-intl/dist/esm/development/server/react-client/index.js). Home() calls
// setRequestLocale for a real reason in production (see its own comment: it opts the route
// into static rendering), but that plumbing itself isn't what this suite is testing, so it's
// stubbed out here the same way a real Next.js RSC render would just make it a no-op.
vi.mock("next-intl/server", async (importOriginal) => ({
  ...(await importOriginal<typeof import("next-intl/server")>()),
  setRequestLocale: vi.fn(),
}));

const NEIGHBORHOODS: NeighborhoodSummary[] = [
  { id: "kadikoy", name: "Kadıköy", boundary: SAMPLE_BOUNDARY, overallScore: 80 },
  { id: "uskudar", name: "Üsküdar", boundary: SAMPLE_BOUNDARY, overallScore: 62 },
  { id: "besiktas", name: "Beşiktaş", boundary: SAMPLE_BOUNDARY, overallScore: null },
];

// Home is an async Server Component (no hooks, no client-only APIs - setRequestLocale just
// writes into a request-scoped cache), so awaiting it and rendering the resolved element
// works fine here - unlike app/[locale]/ilce/[id]/page.tsx, which also calls
// next/navigation's notFound() and can't be exercised this way (see Playwright e2e
// instead). Home itself makes no next-intl *hook* calls (see its own comment) - all
// translated text renders from its child HomeView, so wrapping the render in
// NextIntlClientProvider (Turkish, the default locale, to match this suite's existing
// assertions) is all that's needed alongside the `params` Home now needs. Uses
// createQueryWrapper() (not a bare NextIntlClientProvider) because HomeView now renders
// NeighborhoodSearchableList -> DistrictSearchBar, a client component that calls
// useMutation - in production this is satisfied by app/providers.tsx's app-wide
// QueryClientProvider, which this standalone render of Home doesn't otherwise have.
describe("Home", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  function renderHome(element: React.ReactElement) {
    return render(element, { wrapper: createQueryWrapper() });
  }

  it("lists the three districts, each linking to its score page", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(NEIGHBORHOODS));

    renderHome(await Home({ params: Promise.resolve({ locale: "tr" }) }));

    // NeighborhoodListCard wraps its whole card in one Link (see its own remarks), so each
    // link's accessible name is the full card text, not just the district name - match a
    // substring, same as NeighborhoodListCard.test.tsx does directly. Scoped to the district
    // <ul> specifically (not just screen.getByRole) because the highest/lowest-score
    // HighlightCard above it links to the same district and also matches "Kadıköy" - Kadıköy
    // is this fixture's highest score, so without scoping this query is ambiguous between the
    // highlight callout and the actual list entry the test claims to check.
    const list = screen.getByRole("list");
    const kadikoyLink = within(list).getByRole("link", { name: /Kadıköy/ });
    expect(kadikoyLink).toHaveAttribute("href", "/ilce/kadikoy");
    expect(within(list).getByRole("link", { name: /Üsküdar/ })).toHaveAttribute(
      "href",
      "/ilce/uskudar",
    );
    expect(within(list).getByRole("link", { name: /Beşiktaş/ })).toHaveAttribute(
      "href",
      "/ilce/besiktas",
    );
  });

  it("shows an error message when the district list fails to load", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(null, 500));

    renderHome(await Home({ params: Promise.resolve({ locale: "tr" }) }));

    expect(
      screen.getByText("İlçe listesi yüklenirken bir hata oluştu."),
    ).toBeInTheDocument();
  });
});

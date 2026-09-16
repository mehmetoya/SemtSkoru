import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import Home from "../page";
import { jsonResponse, SAMPLE_BOUNDARY } from "../../../lib/test-utils";
import type { NeighborhoodSummary } from "../../../lib/types";
import trMessages from "../../../messages/tr.json";

const NEIGHBORHOODS: NeighborhoodSummary[] = [
  { id: "kadikoy", name: "Kadıköy", boundary: SAMPLE_BOUNDARY, overallScore: 80 },
  { id: "uskudar", name: "Üsküdar", boundary: SAMPLE_BOUNDARY, overallScore: 62 },
  { id: "besiktas", name: "Beşiktaş", boundary: SAMPLE_BOUNDARY, overallScore: null },
];

// Home is an async Server Component (no hooks, no client-only APIs), so awaiting it and
// rendering the resolved element works fine here - unlike app/[locale]/mahalle/[id]/page.tsx,
// which also calls next/navigation's notFound() and can't be exercised this way (see
// Playwright e2e instead). Home itself makes no next-intl calls (see its own comment) - all
// translated text renders from its child HomeView, so wrapping the render in
// NextIntlClientProvider (Turkish, the default locale, to match this suite's existing
// assertions) is all that's needed - no params/locale plumbing into Home() itself.
describe("Home", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  function renderHome(element: React.ReactElement) {
    return render(
      <NextIntlClientProvider locale="tr" messages={trMessages}>
        {element}
      </NextIntlClientProvider>,
    );
  }

  it("lists the three districts, each linking to its score page", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(NEIGHBORHOODS));

    renderHome(await Home());

    const kadikoyLink = screen.getByRole("link", { name: "Kadıköy" });
    expect(kadikoyLink).toHaveAttribute("href", "/mahalle/kadikoy");
    expect(screen.getByRole("link", { name: "Üsküdar" })).toHaveAttribute(
      "href",
      "/mahalle/uskudar",
    );
    expect(screen.getByRole("link", { name: "Beşiktaş" })).toHaveAttribute(
      "href",
      "/mahalle/besiktas",
    );
  });

  it("shows an error message when the district list fails to load", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(null, 500));

    renderHome(await Home());

    expect(
      screen.getByText("İlçe listesi yüklenirken bir hata oluştu."),
    ).toBeInTheDocument();
  });
});

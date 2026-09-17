import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { NeighborhoodListCard } from "../NeighborhoodListCard";
import { createIntlWrapper, SAMPLE_BOUNDARY } from "../../lib/test-utils";

describe("NeighborhoodListCard", () => {
  it("links to the district's detail page and shows its score band", () => {
    render(
      <NeighborhoodListCard
        id="kadikoy"
        name="Kadıköy"
        boundary={SAMPLE_BOUNDARY}
        overallScore={82}
      />,
      { wrapper: createIntlWrapper() },
    );

    // The whole card is one Link (see NeighborhoodListCard's own remarks), so its accessible
    // name is the card's full text content, not just the district name - match a substring.
    const link = screen.getByRole("link", { name: /Kadıköy/ });
    expect(link).toHaveAttribute("href", "/ilce/kadikoy");
    expect(screen.getByText("82")).toBeInTheDocument();
    expect(screen.getByText("Genel skor: İyi")).toBeInTheDocument();
  });

  it("shows a fallback when the score failed to load", () => {
    render(
      <NeighborhoodListCard
        id="kadikoy"
        name="Kadıköy"
        boundary={SAMPLE_BOUNDARY}
        overallScore={null}
      />,
      { wrapper: createIntlWrapper() },
    );

    expect(screen.getByText("Skor yüklenemedi")).toBeInTheDocument();
  });
});

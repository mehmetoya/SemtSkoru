import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ComparisonSummaryBadge } from "../ComparisonSummaryBadge";
import { createIntlWrapper } from "../../lib/test-utils";

describe("ComparisonSummaryBadge", () => {
  it("renders nothing when there is no summary and nothing is loading", () => {
    const { container } = render(
      <ComparisonSummaryBadge summary={null} isLoading={false} />,
      { wrapper: createIntlWrapper() },
    );
    expect(container).toBeEmptyDOMElement();
  });

  it("shows a loading state while a never-before-seen pair's live Gemini call is in flight", () => {
    render(<ComparisonSummaryBadge summary={undefined} isLoading={true} />, {
      wrapper: createIntlWrapper(),
    });

    expect(screen.getByRole("status")).toHaveTextContent("Karşılaştırma özeti oluşturuluyor…");
  });

  it("shows the summary text and its AI attribution when one exists", () => {
    render(
      <ComparisonSummaryBadge
        summary={{
          text: "Kadıköy hava kalitesinde öne çıkarken, Beşiktaş otoparkta daha güçlü.",
          generatedAt: "2026-09-11T00:00:00Z",
        }}
        isLoading={false}
      />,
      { wrapper: createIntlWrapper() },
    );

    expect(
      screen.getByText("Kadıköy hava kalitesinde öne çıkarken, Beşiktaş otoparkta daha güçlü."),
    ).toBeInTheDocument();
    expect(screen.getByText(/Google Gemini ile oluşturuldu/)).toBeInTheDocument();
  });

  it("keeps showing an already-fetched summary rather than reverting to the loading state on a background refetch", () => {
    render(
      <ComparisonSummaryBadge
        summary={{
          text: "Kadıköy hava kalitesinde öne çıkıyor.",
          generatedAt: "2026-09-11T00:00:00Z",
        }}
        isLoading={true}
      />,
      { wrapper: createIntlWrapper() },
    );

    expect(screen.getByText("Kadıköy hava kalitesinde öne çıkıyor.")).toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("renders nothing once loading settles with no usable summary (e.g. no Gemini key configured, or generation failed)", () => {
    const { container } = render(
      <ComparisonSummaryBadge summary={null} isLoading={false} />,
      { wrapper: createIntlWrapper() },
    );
    expect(container).toBeEmptyDOMElement();
  });
});

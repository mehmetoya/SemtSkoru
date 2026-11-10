import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { DistrictSummaryBadge } from "../DistrictSummaryBadge";
import { createIntlWrapper } from "../../lib/test-utils";

describe("DistrictSummaryBadge", () => {
  it("renders nothing when there is no cached summary yet", () => {
    const { container } = render(<DistrictSummaryBadge summary={null} />, {
      wrapper: createIntlWrapper(),
    });
    expect(container).toBeEmptyDOMElement();
  });

  it("shows the summary text and its AI attribution when one exists", () => {
    render(
      <DistrictSummaryBadge
        summary={{
          text: "Bu ilçe hava kalitesi ve toplu taşıma erişiminde güçlü, otopark bulunabilirliğinde ise zayıf.",
          generatedAt: "2026-09-11T00:00:00Z",
        }}
      />,
      { wrapper: createIntlWrapper() },
    );

    expect(
      screen.getByText(
        "Bu ilçe hava kalitesi ve toplu taşıma erişiminde güçlü, otopark bulunabilirliğinde ise zayıf.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/Google Gemini ile oluşturuldu/)).toBeInTheDocument();
  });
});

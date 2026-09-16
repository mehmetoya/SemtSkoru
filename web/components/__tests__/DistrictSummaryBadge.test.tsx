import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { DistrictSummaryBadge } from "../DistrictSummaryBadge";

describe("DistrictSummaryBadge", () => {
  it("renders nothing when there is no cached summary yet", () => {
    const { container } = render(<DistrictSummaryBadge summary={null} />);
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
    );

    expect(
      screen.getByText(
        "Bu ilçe hava kalitesi ve toplu taşıma erişiminde güçlü, otopark bulunabilirliğinde ise zayıf.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/Google Gemini ile oluşturuldu/)).toBeInTheDocument();
  });
});

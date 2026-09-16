import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { DistrictTrendBadge } from "../DistrictTrendBadge";
import { createIntlWrapper } from "../../lib/test-utils";

describe("DistrictTrendBadge", () => {
  // The state every district is actually in immediately after this feature ships: no baseline
  // snapshot old enough exists yet (see ScoreSnapshotJob), so `trend` is null. This must render
  // as nothing at all - not an empty box, not a loading spinner, not a "check back later"
  // placeholder that implies false precision about a trend that doesn't exist yet.
  it("renders nothing when there is no cached trend yet (the cold-start state)", () => {
    const { container } = render(<DistrictTrendBadge trend={null} />, {
      wrapper: createIntlWrapper(),
    });
    expect(container).toBeEmptyDOMElement();
  });

  it("shows the trend text and its AI attribution when one exists", () => {
    render(
      <DistrictTrendBadge
        trend={{
          text: "Otopark skoru geçen ölçüme göre belirgin şekilde arttı.",
          generatedAt: "2026-09-16T00:00:00Z",
        }}
      />,
      { wrapper: createIntlWrapper() },
    );

    expect(
      screen.getByText("Otopark skoru geçen ölçüme göre belirgin şekilde arttı."),
    ).toBeInTheDocument();
    expect(screen.getByText(/Google Gemini ile oluşturuldu/)).toBeInTheDocument();
  });
});

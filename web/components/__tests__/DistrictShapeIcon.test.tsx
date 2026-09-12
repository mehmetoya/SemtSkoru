import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DistrictShapeIcon } from "../DistrictShapeIcon";
import { SAMPLE_BOUNDARY } from "../../lib/test-utils";

describe("DistrictShapeIcon", () => {
  it("renders an SVG path projected from the boundary's real coordinates", () => {
    const { container } = render(<DistrictShapeIcon boundary={SAMPLE_BOUNDARY} />);

    const path = container.querySelector("path");
    expect(path).not.toBeNull();
    // SAMPLE_BOUNDARY is a closed square - projecting it should produce exactly the 5
    // ring points (4 corners + closing point) mapped into the 0-100 viewBox.
    expect(path!.getAttribute("d")).toBe(
      "M 0.00,100.00 L 0.00,0.00 L 100.00,0.00 L 100.00,100.00 L 0.00,100.00 Z",
    );
  });

  it("renders nothing for a geometry type it doesn't understand", () => {
    const { container } = render(
      <DistrictShapeIcon boundary={{ type: "Point", coordinates: [29, 41] } as GeoJSON.Geometry} />,
    );

    expect(container.querySelector("svg")).toBeNull();
  });
});

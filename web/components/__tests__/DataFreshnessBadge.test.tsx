import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { DataFreshnessBadge } from "../DataFreshnessBadge";

describe("DataFreshnessBadge", () => {
  it("renders nothing when the data is fresh", () => {
    const { container } = render(<DataFreshnessBadge freshness="Fresh" />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when there is no freshness information", () => {
    const { container } = render(<DataFreshnessBadge freshness={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("shows a stale-data warning badge", () => {
    render(<DataFreshnessBadge freshness="Stale" />);
    expect(screen.getByText(/Bayat veri/)).toBeInTheDocument();
  });

  it("shows a historical-data label badge", () => {
    render(<DataFreshnessBadge freshness="Historical" />);
    expect(screen.getByText(/Tarihsel veri/)).toBeInTheDocument();
  });
});

import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { NeighborhoodSearchableList } from "../NeighborhoodSearchableList";
import { createQueryWrapper, jsonResponse, SAMPLE_BOUNDARY } from "../../lib/test-utils";
import type { DistrictSearchResponse, NeighborhoodSummary } from "../../lib/types";

const NEIGHBORHOODS: NeighborhoodSummary[] = [
  { id: "kadikoy", name: "Kadıköy", boundary: SAMPLE_BOUNDARY, overallScore: 80 },
  { id: "uskudar", name: "Üsküdar", boundary: SAMPLE_BOUNDARY, overallScore: 62 },
  { id: "besiktas", name: "Beşiktaş", boundary: SAMPLE_BOUNDARY, overallScore: 71 },
];

// Proves the whole point of this wrapper: it never refetches or duplicates district data (only
// the ids/dimensions the search endpoint returns are new information) and it only ever narrows
// the ALREADY-rendered list down to real ids that search - a fabricated/unknown id in a response
// would simply match nothing here, never crash or add a fake card.
describe("NeighborhoodSearchableList", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  function renderList() {
    return render(<NeighborhoodSearchableList neighborhoods={NEIGHBORHOODS} />, {
      wrapper: createQueryWrapper(),
    });
  }

  function search(query: string) {
    fireEvent.change(screen.getByLabelText("İlçe ara"), { target: { value: query } });
    fireEvent.click(screen.getByRole("button", { name: "Ara" }));
  }

  it("shows every district before any search has been run", () => {
    renderList();

    const list = screen.getByRole("list");
    expect(within(list).getByText("Kadıköy")).toBeInTheDocument();
    expect(within(list).getByText("Üsküdar")).toBeInTheDocument();
    expect(within(list).getByText("Beşiktaş")).toBeInTheDocument();
  });

  it("narrows the list down to only the real matched ids after a successful search", async () => {
    const response: DistrictSearchResponse = {
      status: "Ok",
      message: null,
      matchedIds: ["kadikoy", "besiktas"],
      dimensions: ["airQuality"],
      matchedBy: "Model",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    renderList();

    search("hava kalitesi iyi ilçeler");

    await waitFor(() => expect(screen.getByText("2 ilçe eşleşti")).toBeInTheDocument());
    const list = screen.getByRole("list");
    expect(within(list).getByText("Kadıköy")).toBeInTheDocument();
    expect(within(list).getByText("Beşiktaş")).toBeInTheDocument();
    expect(within(list).queryByText("Üsküdar")).not.toBeInTheDocument();
  });

  it("shows an honest empty state, never all districts, when a search is understood but nothing currently matches", async () => {
    const response: DistrictSearchResponse = {
      status: "Ok",
      message: null,
      matchedIds: [],
      dimensions: ["airQuality"],
      matchedBy: "Model",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    renderList();

    search("hava kalitesi mükemmel ilçeler");

    await waitFor(() =>
      expect(
        screen.getByText(
          "Bu arama için gerçek verilerle eşleşen bir ilçe bulamadık. Farklı bir ifade deneyebilir veya aşağıdaki listeye göz atabilirsiniz.",
        ),
      ).toBeInTheDocument(),
    );
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });

  it("keeps showing every district when the search couldn't be understood, alongside the honest status message", async () => {
    const response: DistrictSearchResponse = {
      status: "NoUsableCriteria",
      message: "backend text",
      matchedIds: [],
      dimensions: [],
      matchedBy: "Model",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    renderList();

    search("en ucuz ilçeler");

    await waitFor(() =>
      expect(
        screen.getByText("Bu sorguyu anlayamadık. Lütfen farklı bir şekilde ifade etmeyi deneyin."),
      ).toBeInTheDocument(),
    );
    // A failed/unusable search never hides the real list behind it.
    const list = screen.getByRole("list");
    expect(within(list).getByText("Kadıköy")).toBeInTheDocument();
    expect(within(list).getByText("Üsküdar")).toBeInTheDocument();
    expect(within(list).getByText("Beşiktaş")).toBeInTheDocument();
  });
});

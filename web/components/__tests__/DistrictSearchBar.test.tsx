import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { DistrictSearchBar } from "../DistrictSearchBar";
import { createQueryWrapper, jsonResponse } from "../../lib/test-utils";
import type { DistrictSearchResponse } from "../../lib/types";

describe("DistrictSearchBar", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  // matchesDistrictName is what the parent tells the bar when the typed text already names a real
  // district; these tests are about the AI preference search, so it defaults to false.
  function renderBar(onResult = vi.fn(), matchesDistrictName = false, onQueryChange = vi.fn()) {
    render(
      <DistrictSearchBar
        onResult={onResult}
        onQueryChange={onQueryChange}
        matchesDistrictName={matchesDistrictName}
      />,
      { wrapper: createQueryWrapper() },
    );
    return onResult;
  }

  function typeQuery(text: string) {
    fireEvent.change(screen.getByLabelText("İlçe ara"), { target: { value: text } });
  }

  it("keeps the submit button disabled until the user types something", () => {
    renderBar();

    const button = screen.getByRole("button", { name: "Ara" });
    expect(button).toBeDisabled();

    typeQuery("hava kalitesi iyi ilçeler");
    expect(button).toBeEnabled();
  });

  it("reports the real matched ids and recognized dimensions back to the parent on a successful search", async () => {
    const response: DistrictSearchResponse = {
      status: "Ok",
      message: null,
      matchedIds: ["kadikoy", "besiktas"],
      dimensions: ["airQuality", "greenSpace"],
      matchedBy: "Model",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    const onResult = renderBar();

    typeQuery("hava kalitesi ve yeşil alanı iyi ilçeler");
    fireEvent.click(screen.getByRole("button", { name: "Ara" }));

    await waitFor(() =>
      expect(onResult).toHaveBeenCalledWith({ matchedIds: ["kadikoy", "besiktas"], dimensions: ["airQuality", "greenSpace"] }),
    );
    // The recognized dimensions surface as human-readable chips, not raw camelCase JSON keys.
    expect(screen.getByText("Hava Kalitesi")).toBeInTheDocument();
    expect(screen.getByText("Yeşil Alan")).toBeInTheDocument();
  });

  it("shows this app's own translated status message, not the backend's raw `message` field, for an unusable query", async () => {
    const response: DistrictSearchResponse = {
      status: "NoUsableCriteria",
      message: "some other backend string that must never be rendered",
      matchedIds: [],
      dimensions: [],
      matchedBy: "Model",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    const onResult = renderBar();

    typeQuery("çok tuhaf bir sorgu");
    fireEvent.click(screen.getByRole("button", { name: "Ara" }));

    await waitFor(() =>
      expect(screen.getByText("Bu sorguyu anlayamadık. Lütfen farklı bir şekilde ifade etmeyi deneyin.")).toBeInTheDocument(),
    );
    expect(
      screen.queryByText("some other backend string that must never be rendered"),
    ).not.toBeInTheDocument();
    // A non-Ok result must clear any previous filter on the parent, never leave it standing.
    expect(onResult).toHaveBeenCalledWith(null);
  });

  it("reports null to the parent when the request fails outright", async () => {
    vi.mocked(fetch).mockRejectedValue(new TypeError("network error"));
    const onResult = renderBar();

    typeQuery("bir sorgu");
    fireEvent.click(screen.getByRole("button", { name: "Ara" }));

    await waitFor(() =>
      expect(screen.getByText("Bir şeyler ters gitti. Lütfen daha sonra tekrar deneyin.")).toBeInTheDocument(),
    );
    expect(onResult).toHaveBeenCalledWith(null);
  });

  it("clears the query and reports null to the parent when Temizle is clicked", async () => {
    const response: DistrictSearchResponse = {
      status: "Ok",
      message: null,
      matchedIds: ["kadikoy"],
      dimensions: ["airQuality"],
      matchedBy: "Model",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    const onResult = renderBar();

    typeQuery("hava kalitesi iyi ilçeler");
    fireEvent.click(screen.getByRole("button", { name: "Ara" }));
    await waitFor(() => expect(screen.getByText("Hava Kalitesi")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: "Temizle" }));

    expect(onResult).toHaveBeenLastCalledWith(null);
    expect(screen.getByLabelText("İlçe ara")).toHaveValue("");
  });

  // When Gemini is unreachable the backend still answers "Ok" with real, score-ranked districts,
  // worked out from its own keyword table. The filtering must therefore behave exactly like a
  // normal search - but the visitor has to be told the query was only keyword-matched.
  it("filters normally but says so plainly when the backend fell back to keyword matching", async () => {
    const response: DistrictSearchResponse = {
      status: "Ok",
      message: null,
      matchedIds: ["kadikoy"],
      dimensions: ["airQuality"],
      matchedBy: "KeywordFallback",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    const onResult = renderBar();

    typeQuery("temiz hava");
    fireEvent.click(screen.getByRole("button", { name: "Ara" }));

    await waitFor(() =>
      expect(onResult).toHaveBeenCalledWith({ matchedIds: ["kadikoy"], dimensions: ["airQuality"] }),
    );
    expect(screen.getByText(/basit anahtar kelime eşleştirmesiyle/)).toBeInTheDocument();
  });

  // The notice is only honest if it stays absent on the normal path.
  it("does not show the keyword-matching notice when the model answered normally", async () => {
    const response: DistrictSearchResponse = {
      status: "Ok",
      message: null,
      matchedIds: ["kadikoy"],
      dimensions: ["airQuality"],
      matchedBy: "Model",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    renderBar();

    typeQuery("temiz hava");
    fireEvent.click(screen.getByRole("button", { name: "Ara" }));

    await waitFor(() => expect(screen.getByText("Hava Kalitesi")).toBeInTheDocument());
    expect(screen.queryByText(/basit anahtar kelime eşleştirmesiyle/)).not.toBeInTheDocument();
  });

  // Typing a district name must never reach the AI endpoint: the page already filters the loaded
  // list by name live, and a request here would spend shared, rate-limited Gemini quota only to
  // answer "couldn't understand that" underneath correct results.
  it("never calls the search endpoint while the query names a district", async () => {
    renderBar(vi.fn(), true);

    typeQuery("Bağcılar");
    fireEvent.click(screen.getByRole("button", { name: "Ara" }));

    await waitFor(() => expect(screen.getByLabelText("İlçe ara")).toHaveValue("Bağcılar"));
    expect(fetch).not.toHaveBeenCalled();
  });

  it("reports every keystroke upward so the parent can filter by name as the user types", () => {
    const onQueryChange = vi.fn();
    renderBar(vi.fn(), false, onQueryChange);

    typeQuery("Bağ");

    expect(onQueryChange).toHaveBeenLastCalledWith("Bağ");
  });
});

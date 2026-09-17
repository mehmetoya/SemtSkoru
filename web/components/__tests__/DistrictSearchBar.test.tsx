import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { DistrictSearchBar } from "../DistrictSearchBar";
import { createQueryWrapper, jsonResponse } from "../../lib/test-utils";
import type { DistrictSearchResponse } from "../../lib/types";

describe("DistrictSearchBar", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  function renderBar(onResult = vi.fn()) {
    render(<DistrictSearchBar onResult={onResult} />, { wrapper: createQueryWrapper() });
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
});

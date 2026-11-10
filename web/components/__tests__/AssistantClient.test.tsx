import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { AssistantClient } from "../AssistantClient";
import { createQueryWrapper, jsonResponse } from "../../lib/test-utils";
import type { AssistantResponse, NeighborhoodScore } from "../../lib/types";

function emptyDimension() {
  return { score: null, freshness: null, sourceName: null, publishedAt: null };
}

function fullScore(overrides: Partial<NeighborhoodScore> = {}): NeighborhoodScore {
  const dim = { score: 83, freshness: "Fresh" as const, sourceName: "İBB Hava Kalitesi", publishedAt: "2026-09-11T00:00:00Z" };
  return {
    neighborhoodId: "kadikoy",
    airQuality: dim,
    greenSpace: emptyDimension(),
    transportation: dim,
    parking: dim,
    healthAccess: dim,
    transitAccess: dim,
    overall: 83,
    isComplete: false,
    summary: null,
    trend: null,
    ...overrides,
  };
}

describe("AssistantClient", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  function renderClient() {
    return render(<AssistantClient />, { wrapper: createQueryWrapper() });
  }

  function typePrompt(text: string) {
    fireEvent.change(screen.getByLabelText("Ne arıyorsun?"), { target: { value: text } });
  }

  it("keeps the submit button disabled until the user types something", () => {
    renderClient();

    const button = screen.getByRole("button", { name: "Öner" });
    expect(button).toBeDisabled();

    typePrompt("Hava kalitesi önemli");
    expect(button).toBeEnabled();
  });

  it("fills the textarea when an example prompt is clicked", () => {
    renderClient();

    fireEvent.click(screen.getByRole("button", { name: /Çocuklu bir aileyim/ }));

    expect(screen.getByLabelText("Ne arıyorsun?")).toHaveValue(
      "Çocuklu bir aileyim, yeşil alan ve sağlık erişimi önemli, bütçem sınırlı.",
    );
  });

  it("shows the AI disclosure label and the real score data behind a grounded recommendation", async () => {
    const response: AssistantResponse = {
      status: "Ok",
      message: null,
      recommendations: [
        {
          neighborhoodId: "kadikoy",
          neighborhoodName: "Kadıköy",
          reasoning: "Hava kalitesi skoru 83 ile yüksek.",
          score: fullScore(),
        },
      ],
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    renderClient();

    typePrompt("Hava kalitesi önemli");
    fireEvent.click(screen.getByRole("button", { name: "Öner" }));

    await waitFor(() => expect(screen.getByText("Kadıköy")).toBeInTheDocument());
    expect(screen.getByText(/Google Gemini ile oluşturuldu/)).toBeInTheDocument();
    expect(screen.getByText("Hava kalitesi skoru 83 ile yüksek.")).toBeInTheDocument();
    // The real score numbers, not just AI prose - the overall badge and dimension bars.
    expect(screen.getAllByText("83").length).toBeGreaterThan(0);
    expect(screen.getByText("Veri yok")).toBeInTheDocument();
  });

  it("shows this app's own translated status message, not the backend's raw `message` field, when no usable recommendation comes back", async () => {
    // `message` is deliberately NOT what AssistantClient renders (see its own comment) -
    // it's backend-authored Turkish and can't be localized from here. AssistantClient maps
    // `status` to messages/tr.json's own Assistant.status.NoUsableRecommendations instead,
    // which happens to read identically to the backend's Turkish message by design (same
    // honest meaning) - this response's `message` is set to something else entirely to
    // prove the assertion below is checking the frontend's translation, not an accidental
    // pass-through.
    const response: AssistantResponse = {
      status: "NoUsableRecommendations",
      message: "some other backend string that must never be rendered",
      recommendations: [],
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));
    renderClient();

    typePrompt("çok tuhaf bir istek");
    fireEvent.click(screen.getByRole("button", { name: "Öner" }));

    await waitFor(() =>
      expect(
        screen.getByText("İsteğiniz için güvenilir bir öneri oluşturamadık. İlçeleri doğrudan karşılaştırmayı deneyebilirsiniz."),
      ).toBeInTheDocument(),
    );
    expect(
      screen.queryByText("some other backend string that must never be rendered"),
    ).not.toBeInTheDocument();
  });

  it("shows a generic Turkish error message when the request fails outright", async () => {
    vi.mocked(fetch).mockRejectedValue(new TypeError("network error"));
    renderClient();

    typePrompt("bir istek");
    fireEvent.click(screen.getByRole("button", { name: "Öner" }));

    await waitFor(() =>
      expect(screen.getByText("Bir şeyler ters gitti. Lütfen daha sonra tekrar deneyin.")).toBeInTheDocument(),
    );
  });
});

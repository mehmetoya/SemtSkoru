import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { useAssistant } from "../useAssistant";
import { createQueryWrapper, jsonResponse } from "../../test-utils";
import type { AssistantResponse } from "../../types";

describe("useAssistant", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("posts the free-text prompt and locale as JSON and returns the parsed response", async () => {
    const response: AssistantResponse = { recommendations: [], status: "Ok", message: null };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));

    const { result } = renderHook(() => useAssistant("tr"), { wrapper: createQueryWrapper() });

    act(() => {
      result.current.mutate("Hava kalitesi önemli");
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(response);
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5169/api/asistan",
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ prompt: "Hava kalitesi önemli", locale: "tr" }),
      }),
    );
  });

  // Proves the locale the hook is constructed with (mirroring useLocale() in AssistantClient) is
  // what actually reaches the request body, not just a hardcoded default.
  it("posts the requested locale, not just the default", async () => {
    const response: AssistantResponse = { recommendations: [], status: "Ok", message: null };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response));

    const { result } = renderHook(() => useAssistant("en"), { wrapper: createQueryWrapper() });

    act(() => {
      result.current.mutate("Air quality matters");
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(fetch).toHaveBeenCalledWith(
      "http://localhost:5169/api/asistan",
      expect.objectContaining({
        body: JSON.stringify({ prompt: "Air quality matters", locale: "en" }),
      }),
    );
  });

  // A 503/429/etc. response still carries a structured, parseable AssistantResponse body (see
  // backend/src/SemtSkoru.Api/Endpoints/AssistantEndpoints.cs) - this must resolve as data the UI
  // can render honestly, not reject as a generic mutation error.
  it("resolves successfully for a non-Ok status carrying a Turkish message", async () => {
    const response: AssistantResponse = {
      recommendations: [],
      status: "NotConfigured",
      message: "AI Semt Asistanı şu anda yapılandırılmamış.",
    };
    vi.mocked(fetch).mockResolvedValue(jsonResponse(response, 503));

    const { result } = renderHook(() => useAssistant("tr"), { wrapper: createQueryWrapper() });
    act(() => {
      result.current.mutate("bir istek");
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.status).toBe("NotConfigured");
    expect(result.current.data?.message).toBe("AI Semt Asistanı şu anda yapılandırılmamış.");
  });

  it("rejects when the request fails at the network level", async () => {
    vi.mocked(fetch).mockRejectedValue(new TypeError("network error"));

    const { result } = renderHook(() => useAssistant("tr"), { wrapper: createQueryWrapper() });
    act(() => {
      result.current.mutate("bir istek");
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

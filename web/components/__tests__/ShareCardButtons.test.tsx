import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { ShareCardButtons } from "../ShareCardButtons";

const PROPS = {
  imageUrl: "/mahalle/kadikoy/kart",
  fileName: "semtskoru-kadikoy.png",
  shareTitle: "Kadıköy Yaşam Skoru | SemtSkoru",
  shareText: "Kadıköy ilçesinin SemtSkoru yaşam skorunu incele.",
  fallbackUrl: "https://semtskoru.vercel.app/mahalle/kadikoy",
};

describe("ShareCardButtons", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders a downloadable link pointing straight at the image route, no JS required", () => {
    render(<ShareCardButtons {...PROPS} />);

    const link = screen.getByRole("link", { name: "İndir" });
    expect(link).toHaveAttribute("href", PROPS.imageUrl);
    expect(link).toHaveAttribute("download", PROPS.fileName);
  });

  it("shares the actual image file when the Web Share API supports files", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    const canShare = vi.fn().mockReturnValue(true);
    vi.stubGlobal("navigator", { ...navigator, share, canShare });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(new Blob(["fake-png"]), { status: 200 })),
    );

    render(<ShareCardButtons {...PROPS} />);
    fireEvent.click(screen.getByRole("button", { name: "Paylaş" }));

    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    const call = share.mock.calls[0][0];
    expect(call.title).toBe(PROPS.shareTitle);
    expect(call.files).toHaveLength(1);
    expect(call.files[0].name).toBe(PROPS.fileName);
    expect(call.url).toBeUndefined();
  });

  it("falls back to sharing the link when the browser can't share files", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    const canShare = vi.fn().mockReturnValue(false);
    vi.stubGlobal("navigator", { ...navigator, share, canShare });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(new Blob(["fake-png"]), { status: 200 })),
    );

    render(<ShareCardButtons {...PROPS} />);
    fireEvent.click(screen.getByRole("button", { name: "Paylaş" }));

    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    expect(share).toHaveBeenCalledWith({
      title: PROPS.shareTitle,
      text: PROPS.shareText,
      url: PROPS.fallbackUrl,
    });
  });

  it("copies the fallback link to the clipboard when navigator.share doesn't exist", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { ...navigator, share: undefined, clipboard: { writeText } });

    render(<ShareCardButtons {...PROPS} />);
    fireEvent.click(screen.getByRole("button", { name: "Paylaş" }));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith(PROPS.fallbackUrl));
    expect(await screen.findByText("Bağlantı panoya kopyalandı.")).toBeInTheDocument();
  });

  it("stays silent when the user simply cancels the native share sheet", async () => {
    const abortError = Object.assign(new Error("cancelled"), { name: "AbortError" });
    const share = vi.fn().mockRejectedValue(abortError);
    const canShare = vi.fn().mockReturnValue(true);
    vi.stubGlobal("navigator", { ...navigator, share, canShare });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(new Blob(["fake-png"]), { status: 200 })),
    );

    render(<ShareCardButtons {...PROPS} />);
    fireEvent.click(screen.getByRole("button", { name: "Paylaş" }));

    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    expect(screen.queryByText(/Paylaşım tamamlanamadı/)).not.toBeInTheDocument();
  });
});

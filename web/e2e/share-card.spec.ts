import { test, expect } from "@playwright/test";

// Real backend, real browser, like compare-flow.spec.ts - proves the actual ImageResponse
// routes render a real PNG and that the download/share buttons work against them, not
// just that the mocked unit tests believe they do.
test.use({ permissions: ["clipboard-read", "clipboard-write"] });

const PNG_SIGNATURE = "89504e470d0a1a0a";

test("downloads a real PNG score card and falls back to copying a link when sharing", async ({ page }) => {
  await page.goto("/ilce/kadikoy");
  await expect(page.getByRole("heading", { name: "Kadıköy" })).toBeVisible();

  const [download] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("link", { name: "İndir" }).click(),
  ]);
  expect(download.suggestedFilename()).toBe("semtskoru-kadikoy.png");
  const path = await download.path();
  expect(path).toBeTruthy();
  const fs = await import("node:fs");
  const bytes = fs.readFileSync(path!);
  expect(bytes.subarray(0, 8).toString("hex")).toBe(PNG_SIGNATURE);

  // Headless Chromium under Playwright has no navigator.share, so this exercises the
  // clipboard-copy fallback path, not the Web Share API itself.
  await page.getByRole("button", { name: "Paylaş" }).click();
  await expect(page.getByRole("status")).toHaveText("Bağlantı panoya kopyalandı.");
  const clipboardText = await page.evaluate(() => navigator.clipboard.readText());
  expect(clipboardText).toBe("https://semtskoru.vercel.app/ilce/kadikoy");
});

test("downloads a real PNG comparison card once two districts are selected", async ({ page }) => {
  await page.goto("/karsilastir");
  await page.getByLabel("Birinci ilçe", { exact: true }).selectOption("kadikoy");
  await page.getByLabel("İkinci ilçe", { exact: true }).selectOption("uskudar");
  await expect(page.getByTestId("comparison-result")).toBeVisible();

  const [download] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("link", { name: "İndir" }).click(),
  ]);
  expect(download.suggestedFilename()).toBe("semtskoru-karsilastirma-kadikoy-uskudar.png");
  const path = await download.path();
  const fs = await import("node:fs");
  const bytes = fs.readFileSync(path!);
  expect(bytes.subarray(0, 8).toString("hex")).toBe(PNG_SIGNATURE);
});

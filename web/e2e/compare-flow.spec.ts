import { test, expect } from "@playwright/test";

test("search for a district, then compare two districts", async ({ page }) => {
  await page.goto("/");

  const kadikoyLink = page.getByRole("link", { name: "Kadıköy" });
  await expect(kadikoyLink).toBeVisible();
  await expect(page.getByRole("link", { name: "Üsküdar" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Beşiktaş" })).toBeVisible();

  await kadikoyLink.click();
  await expect(page).toHaveURL(/\/mahalle\/kadikoy$/);
  await expect(page.getByRole("heading", { name: "Kadıköy" })).toBeVisible();
  await expect(page.getByText("Genel skor")).toBeVisible();

  await page.goto("/karsilastir");
  await page.getByLabel("Birinci ilçe").selectOption("kadikoy");
  await page.getByLabel("İkinci ilçe").selectOption("uskudar");

  const table = page.locator("table");
  await expect(table).toBeVisible();
  await expect(table.getByText("Kadıköy")).toBeVisible();
  await expect(table.getByText("Üsküdar")).toBeVisible();
  await expect(table.getByText("Genel Skor")).toBeVisible();

  // The map only ever renders in a real browser (WebGL), which is exactly what this
  // e2e run is -- unlike the jsdom-based component tests, this proves it actually paints.
  const mapCanvas = page.locator('[data-testid="neighborhood-map"] canvas');
  await expect(mapCanvas).toBeVisible();
});

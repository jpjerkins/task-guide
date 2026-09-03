import { test, expect } from "@playwright/test";

test("landing page loads and lists tasks", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveTitle(/Task Guide/i);
});

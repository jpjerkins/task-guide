import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./specs",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "https://pi5.taile6b761.ts.net",
  },
});

import { defineConfig, devices } from "@playwright/test";

/**
 * E2E (spec §16 M7) — chạy trên DEV stack, KHÔNG phải prod:
 *   make dev-deps          # db :5433 + gotenberg :3000
 *   make api               # API :8080 — BẮT BUỘC kèm env để giả lập đăng nhập Google:
 *                          #   Auth__DevFakeGoogle=true Auth__AdminEmails=e2e-admin@hoclieu.dev
 *   make web               # vite :5173
 * Sau đó: pnpm e2e (hoặc make e2e)
 *
 * Chromium download bị chặn trong mạng này → dùng Chrome hệ thống (channel "chrome").
 * Viewport mobile (học sinh làm bài) tạo context riêng trong test, không cần project riêng.
 */
export default defineConfig({
  testDir: "./e2e",
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:5173",
    channel: "chrome",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chrome", use: { ...devices["Desktop Chrome"] } }],
});

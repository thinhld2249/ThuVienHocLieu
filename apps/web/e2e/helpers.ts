import type { BrowserContext } from "@playwright/test";
import dayjs from "dayjs";

export const ADMIN_EMAIL = "e2e-admin@hoclieu.dev";
export const TEACHER_EMAIL = "e2e-teacher@hoclieu.dev";
export const STUDENT_NAME = "Học Sinh E2E";

/**
 * "Đăng nhập giả lập" (spec §16): POST /api/auth/google với token `devfake:{sub}:{email}[:{name}]`.
 * Chỉ hợp lệ khi API chạy với `Auth__DevFakeGoogle=true` (chế độ dev/E2E, không có ở prod).
 * Dùng `context.request` để Set-Cookie rơi vào cookie jar của context (cùng host :5173 qua vite proxy).
 */
export async function fakeLogin(
  context: BrowserContext,
  email: string,
  name?: string,
): Promise<{ systemRole: string; status: string }> {
  const sub = "e2e-" + email.replace(/[^a-z0-9]/gi, "");
  const idToken = `devfake:${sub}:${email}${name ? ":" + name : ""}`;
  const resp = await context.request.post("/api/auth/google", {
    data: { idToken },
    headers: { "X-Requested-With": "hoclieu" },
  });
  if (!resp.ok()) {
    throw new Error(
      `Đăng nhập ${email} thất bại: HTTP ${resp.status()} ${await resp.text()}`,
    );
  }
  return resp.json();
}

/** Giá trị `datetime-local` (giờ local máy test) = giờ hiện tại + N phút. */
export function localInputPlusMinutes(minutes: number): string {
  return dayjs().add(minutes, "minute").format("YYYY-MM-DDTHH:mm");
}

/** CSRF header bắt buộc cho mọi request ghi (spec §3.4). */
export const CSRF = { "X-Requested-With": "hoclieu" } as const;

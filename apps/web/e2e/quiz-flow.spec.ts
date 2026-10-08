import { devices, expect, test } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  ADMIN_EMAIL,
  CSRF,
  fakeLogin,
  localInputPlusMinutes,
  STUDENT_NAME,
  TEACHER_EMAIL,
} from "./helpers";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

/**
 * Luồng E2E chính (spec §16 M7): đăng nhập giả lập → GV tạo quiz từ Word
 * → hẹn giờ/không mở → hiện ngay → học sinh làm bài trên viewport mobile
 * (tải lại không mất đáp án) → nộp → kết quả → GV xem bảng kết quả.
 */

const TPL_DOCX = path.join(
  __dirname,
  "..",
  "public",
  "templates",
  "mau-bai-tap.docx",
);

interface QuizInfo {
  id: number;
  slug: string;
  title: string;
  questionCount: number;
}

let quiz: QuizInfo | null = null;
let studentScore: string | null = null;

test.describe
  .serial("Quiz: Word → hiển thị → học sinh làm bài → kết quả", () => {
  test("Admin: dashboard + bật auto-approve domain + duyệt sẵn GV", async ({
    browser,
  }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const me = await fakeLogin(ctx, ADMIN_EMAIL, "Admin E2E");
    expect(me.systemRole).toBe("Admin");
    expect(me.status).toBe("Active");

    // Duyệt sẵn GV nếu run trước để lại trạng thái Pending (idempotent)
    const search = await ctx.request.get(
      `/api/admin/users?q=${encodeURIComponent(TEACHER_EMAIL)}`,
    );
    expect(search.ok()).toBeTruthy();
    const paged = await search.json();
    for (const u of (paged.items ?? []) as {
      id: number;
      email: string;
      status: string;
    }[]) {
      if (u.email.toLowerCase() === TEACHER_EMAIL && u.status !== "Active") {
        const p = await ctx.request.patch(`/api/admin/users/${u.id}`, {
          data: { status: "Active", teamIds: [] },
          headers: CSRF,
        });
        expect(p.ok()).toBeTruthy();
      }
    }

    // Domain hoclieu.dev tự duyệt → run sau GV luôn Active khi đăng nhập mới
    const get = await ctx.request.get("/api/admin/settings");
    const settings = ((await get.json()).settings ?? {}) as Record<
      string,
      unknown
    >;
    settings["auth.auto_approve_domains"] = ["hoclieu.dev"];
    const put = await ctx.request.put("/api/admin/settings", {
      data: { settings },
      headers: CSRF,
    });
    expect(put.ok()).toBeTruthy();

    await page.goto("/admin");
    await expect(page.getByText("Hàng chờ duyệt")).toBeVisible();
    await ctx.close();
  });

  test("GV: tạo quiz từ file Word (.docx) → màn rà soát", async ({
    browser,
  }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await fakeLogin(ctx, TEACHER_EMAIL, "Giáo Viên E2E");

    await page.goto("/gv/bai-tap");
    await page
      .locator('input[type="file"][accept=".docx"]')
      .setInputFiles(TPL_DOCX);

    // Import xong tự chuyển về trang rà soát /gv/bai-tap/:id?imported=1
    await page.waitForURL(/\/gv\/bai-tap\/\d+\?imported=1/);
    await expect(page.getByText("Đã tạo bài tập từ file")).toBeVisible();

    const id = Number(page.url().match(/\/gv\/bai-tap\/(\d+)/)?.[1]);
    const get = await ctx.request.get(`/api/teacher/quizzes/${id}`);
    expect(get.ok()).toBeTruthy();
    const q = (await get.json()) as QuizInfo;
    quiz = q;
    expect(quiz.questionCount).toBeGreaterThan(0);
    expect(quiz.slug.length).toBeGreaterThan(0);
    await ctx.close();
  });

  test("GV: hẹn giờ → 'Sắp mở' (khách chưa làm được) → Hiện ngay", async ({
    browser,
  }) => {
    test.skip(quiz == null, "quiz chưa được tạo");
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await fakeLogin(ctx, TEACHER_EMAIL);

    await page.goto(`/gv/bai-tap/${quiz!.id}`);
    await page.getByRole("tab", { name: "Hiển thị" }).click();

    // Hẹn giờ tùy chỉnh: mở T+1ph → đóng T+3ph (giờ local)
    await page.getByRole("button", { name: "Hẹn giờ" }).click();
    const pop = page.getByRole("dialog", { name: "Hẹn giờ hiển thị" });
    await pop
      .locator('input[type="datetime-local"]')
      .nth(0)
      .fill(localInputPlusMinutes(2));
    await pop
      .locator('input[type="datetime-local"]')
      .nth(1)
      .fill(localInputPlusMinutes(5));
    await pop.getByRole("button", { name: "Lên lịch" }).click();
    await expect(page.getByText("Sắp mở", { exact: true })).toBeVisible();

    // Trước khung giờ: khách vào trang quiz không làm được bài
    const guest = await browser.newContext();
    const gp = await guest.newPage();
    await gp.goto(`/bai-tap/${quiz!.slug}-${quiz!.id}`);
    await expect(gp.getByText("Bài tập hiện không mở")).toBeVisible();
    await expect(
      gp.getByRole("button", { name: "Bắt đầu làm bài" }),
    ).toHaveCount(0);
    await guest.close();

    // Hiện ngay → khách làm được
    // ("Đang hiện" hiện ở cả badge lẫn dòng trạng thái tab Hiển thị → first())
    await page.getByRole("button", { name: "Hiện", exact: true }).click();
    await expect(
      page.getByText("Đang hiện", { exact: true }).first(),
    ).toBeVisible();
    await ctx.close();
  });

  test("Học sinh (mobile): làm bài, tải lại không mất đáp án, nộp → kết quả", async ({
    browser,
  }) => {
    test.skip(quiz == null, "quiz chưa được tạo");
    const ctx = await browser.newContext({ ...devices["iPhone 13"] });
    const page = await ctx.newPage();

    await page.goto(`/bai-tap/${quiz!.slug}-${quiz!.id}`);
    await expect(page.getByText("Đang mở")).toBeVisible();
    await page.locator("#guest-name").fill(STUDENT_NAME);
    await page.getByRole("button", { name: "Bắt đầu làm bài" }).click();
    await page.waitForURL(/\/lam-bai\/[0-9a-f]{8}-[0-9a-f-]{27}/);

    const total = Number(
      await page.locator('[role="progressbar"]').getAttribute("aria-valuemax"),
    );
    expect(total).toBe(quiz!.questionCount);

    // Chọn phương án đầu của từng câu, đi tới bằng "Câu sau"
    for (let i = 1; i <= total; i++) {
      const opts = page.locator(`[aria-label="Các phương án câu ${i}"]`);
      await opts.locator("button").first().click();
      if (i === 1) {
        // Chờ autosave (debounce 1,5s) rồi tải lại: đáp án phải còn (localStorage + server)
        await page.waitForTimeout(2500);
        await page.reload();
        await expect(
          page
            .locator(`[aria-label="Các phương án câu 1"]`)
            .locator("button")
            .first(),
        ).toHaveAttribute("aria-checked", "true");
      }
      if (i < total) {
        await page.getByRole("button", { name: "Câu sau" }).click();
      } else {
        await page.getByRole("button", { name: "Nộp bài" }).first().click();
        const dialog = page.getByRole("dialog", { name: "Xác nhận nộp bài" });
        await dialog.getByRole("button", { name: "Nộp bài" }).click();
      }
    }

    await page.waitForURL(/\/ket-qua\/[0-9a-f]{8}-[0-9a-f-]{27}/);
    const scoreEl = page.locator('[aria-label^="Điểm 10:"]');
    await expect(scoreEl).toBeVisible();
    studentScore = (await scoreEl.getAttribute("aria-label")) ?? null;
    await expect(page.getByText("điểm thang 10")).toBeVisible();
    await ctx.close();
  });

  test("GV: tab Kết quả hiện lượt làm của học sinh", async ({ browser }) => {
    test.skip(quiz == null, "quiz chưa được tạo");
    expect(studentScore, "kết quả học sinh chưa được ghi nhận").toBeTruthy();
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await fakeLogin(ctx, TEACHER_EMAIL);

    await page.goto(`/gv/bai-tap/${quiz!.id}`);
    await page.getByRole("tab", { name: "Kết quả" }).click();
    await expect(page.getByText("Họ tên")).toBeVisible();
    await expect(page.getByText(STUDENT_NAME)).toBeVisible();
    await ctx.close();
  });
});

# Học Liệu — cổng học liệu tiểu học

Cổng học liệu tối giản cho ~500 giáo viên: tài liệu (bài giảng, KHBD, PPCT…), bài tập trắc nghiệm tạo từ Word/Excel, lớp học & giao bài bằng mã/QR, tổ chuyên môn, quản trị. Học sinh/phụ huynh dùng không cần tài khoản.

> Đặc tả đầy đủ: [`qwen.md`](qwen.md) · Quyết định thiết kế: [`docs/decisions.md`](docs/decisions.md)

## Stack

| Lớp      | Công nghệ                                                                                                             |
| -------- | --------------------------------------------------------------------------------------------------------------------- |
| Frontend | React 19 · TypeScript · Vite · React Router 7 · TanStack Query 5 · Tailwind CSS 4 + shadcn/ui · react-hook-form + zod |
| Backend  | .NET 8 · ASP.NET Core Minimal APIs · EF Core + PostgreSQL 17 · Google sign-in · Cloudinary                            |
| File     | Cloudinary (lưu trữ/preview) · Gotenberg 8 (Office → PDF)                                                             |
| Chạy     | Docker Compose 1 VM · Caddy (HTTPS) · GitHub Actions → GHCR                                                           |

## Cấu trúc

```
apps/web/   React SPA (pnpm)
apps/api/   .NET solution (HocLieu.Api, HocLieu.QuizImport, tests)
deploy/     docker-compose.yml, .env.example, gotenberg/
docs/       decisions.md, quiz-import-format.md, huong-dan-giao-vien.md, van-hanh.md
```

## Chạy dev

Yêu cầu: .NET 8 SDK, Node 22+, pnpm 10, Docker.

```bash
# 1. db + gotenberg (localhost:5433, :3000 — 5433 vì 5432 hay bị PostgreSQL cá nhân chiếm)
make dev-deps

# 2. API :8080 (tự migrate + seed ở lần đầu — appsettings.Development)
make api

# 3. FE :5173 (proxy /api → :8080)
make web
```

Trên Windows không có `make`: chạy thẳng

```powershell
docker compose -f docker-compose.dev.yml up -d
cd apps/api; dotnet watch --project src/HocLieu.Api
cd apps/web; pnpm dev
```

Các lệnh khác: `make gen-api` (sinh `src/lib/api-types.ts` — cần API đang chạy), `make test`, `make migration name=...`.

## Cấu hình

API đọc từ env / `appsettings.json` (chi tiết: `apps/api/src/HocLieu.Api/appsettings.json`):

| Biến                                   | Ý nghĩa                                                            |
| -------------------------------------- | ------------------------------------------------------------------ |
| `ConnectionStrings__Default`           | chuỗi kết nối PostgreSQL                                           |
| `Db__MigrateOnStartup`                 | migrate + seed khi khởi động (prod: `true`)                        |
| `Auth__GoogleClientId`                 | OAuth Client ID (FE dùng cùng giá trị qua `VITE_GOOGLE_CLIENT_ID`) |
| `Auth__AdminEmails`                    | email Admin khởi tạo, phân tách bằng dấu phẩy                      |
| `Auth__AllowedDomains`                 | giới hạn domain Google (trống = mọi tài khoản)                     |
| `Cloudinary__Url` / `Cloudinary__Root` | `cloudinary://KEY:SECRET@CLOUD` / thư mục gốc                      |
| `Gotenberg__Url`                       | mặc định `http://gotenberg:3000` (prod)                            |
| `Security__IpHashSalt`                 | salt băm IP (32 byte ngẫu nhiên)                                   |

Production: `cp deploy/.env.example deploy/.env` → điền → `make up` (yêu cầu `SITE_DOMAIN` có DNS trỏ về VM).

Deploy lên Render (thay cho 1 VM): blueprint `render.yaml` (3 Docker service + Postgres quản trị) — hướng dẫn tại [`docs/render-deploy.md`](docs/render-deploy.md).

## Kiểm thử

```bash
make test   # dotnet test (Testcontainers) + pnpm test (Vitest)
make e2e    # Playwright (M7)
```

## Milestones

Trạng thái theo `qwen.md §16`:

- [x] M0 Nền tảng — monorepo, compose, CI, health, OpenAPI, seed, layout FE + tokens, trang chủ
  - Verify 2026-10-07: dev stack (db :5433 + gotenberg :3000), API :8080, FE :5173; prod stack `compose up -d` chạy qua HTTPS `hoclieu.localhost` (home 200, `/api/public/taxonomy` 200 có seed, `/health/live` 200, `/health/ready` 200 Degraded — chưa cấu hình Cloudinary).
  - Lưu ý sandbox: image prod API/web build từ publish/dist trên host vì mạng container không tới nuget/npm (xem `docs/decisions.md`); CI đã mô phỏng xanh local (`dotnet build/test` Release + `pnpm lint/typecheck/test/build`), chưa chạy trên GitHub.
- [x] M1 Đăng nhập Google + vòng đời tài khoản
  - Verify 2026-10-07: BE 23/23 integration tests (8 Smoke + 15 Auth, Testcontainers Postgres 17, `FakeGoogleTokenValidator` thay `GoogleJsonWebSignature`); FE 17/17 unit tests + `pnpm build` xanh (bundle công khai 165 KB gzip).
  - Luồng đã test: user mới → `Pending` + cookie phiên hoạt động; email trong `Auth__AdminEmails` → `Admin`+`Active`; domain tự duyệt → `Active`; `Suspended`/`Rejected` → 403 kèm lý do; token Google sai domain → 403; thiếu CSRF header → 403; `logout-all` đổi `security_stamp` → phiên cũ hết hiệu lực; phiên sống qua "restart" (DataProtection keys lưu PostgreSQL); `PUT /api/me` (họ tên, SĐT, xin vào tổ); lời mời: token hợp lệ → `Active` + vào tổ, email không khớp → 400 kèm email đã mask.
  - FE: `/dang-nhap` (nút Google, chặn webview Zalo/FB kèm hướng dẫn), `/cho-duyet` (form họ tên + SĐT + chọn tổ), `/moi/:token`, guards `RequireAuth`/`RequireActive`/`RequireAdmin`, chip đăng nhập ở header.
  - API chưa đăng nhập trả 401 (không 302) — override `OnRedirectToLogin` (xem `docs/decisions.md`); verify prod: `/api/me` → 401, `/api/public/teams` → 200, SPA 200 qua `https://hoclieu.localhost`.
- [x] M2 Tổ & lời mời
  - Verify 2026-10-07: BE 47/47 integration tests (24 TeamTests, Testcontainers Postgres 17); FE 24/24 unit tests + `pnpm typecheck`/`lint` 0 lỗi + `pnpm build` xanh.
  - Luồng đã test (BE): CRUD tổ (Admin); bổ nhiệm/gỡ tổ trưởng (Admin; mời làm Lead tự hạ Lead cũ); duyệt/từ chối/xin vào tổ (kể cả hàng loạt); lời mời nhiều email (Active thêm thẳng, Pending duyệt luôn, chưa có user → link `/moi/{token}`, hết hạn/thu hồi/gửi lại); GV không phải Lead/Deputy gọi API quản trị → 403; một tổ chỉ có 1 tổ trưởng; thông báo in-app (tạo, đánh dấu đọc).
  - FE: `/gv/to` (chọn tổ), `/gv/to/:teamId` (5 tab: Thành viên · Chờ duyệt · Lời mời · Thông báo tổ · Thống kê — thao tác theo vai trò Lead/Deputy/Member), `/admin/to` (CRUD tổ + bổ nhiệm tổ trưởng qua tìm kiếm GV), `/admin/loi-moi` (mời mọi tổ, mọi vai trò + danh sách lời mời), NotificationBell ở sidebar GV & header Admin.
  - Verify prod: image `hoclieu-{api,web}:local` rebuild + `compose up -d` — `/health/live` 200, `/health/ready` 200, `/api/me` & `/api/teams/1` → 401, `/api/public/teams` 200, SPA 200, `/openapi/v1.json` (16 KB) có `/api/admin/users`, log "Đã migrate + seed database", không lỗi.
  - Sửa phụ: Caddy proxy thêm `/openapi/*` (trước đó rơi vào SPA fallback).
- [x] M3 Tài liệu (upload, preview, CRUD, tìm kiếm)
  - Verify 2026-10-07: BE 68/68 integration tests (Testcontainers Postgres 17); FE 28/28 unit tests + `typecheck`/`lint` 0 lỗi + `build` xanh (bundle công khai 190 KB gzip < chỉ tiêu 250 KB).
  - Luồng đã test (BE): upload đa file + kiểm magic bytes, xử lý preview (Office → PDF qua Gotenberg, PDF/ảnh/video), thumbnail; CRUD tài liệu + xóa mềm/ khôi phục/ nhân bản (30 ngày); phạm vi Public/Teachers/Team/Private (khách & GV tổ khác nhận 404); hiện/ẩn/hẹn giờ + trạng thái tính theo giờ VN; tìm kiếm không dấu ("ke hoach" khớp "Kế hoạch"); khách chỉ tải được khi tài liệu cho phép; yêu thích; báo lỗi nội dung.
  - FE: `/gv/tai-lieu` (bảng + thao tác hàng loạt Hiện/Ẩn/Hẹn giờ + thùng rác), form tạo/sửa (kéo-thả nhiều file ≤10 có tiến trình, ảnh bìa, scope + team, hẹn giờ theo giờ VN), `/gv/yeu-thich`, khu công khai `/`, `/khoi/:grade`, `/chuyen-muc/:section`, `/tai-lieu/:slug-:id` (preview từng trang, liên quan, báo lỗi), `/tim-kiem` (gộp tài liệu + bài tập).
  - Verify prod: image `hoclieu-{api,web}:local` rebuild (dist mới) + `compose up -d` qua `https://hoclieu.localhost` — SPA routes 200, `/api/public/home` · `taxonomy` · `items` 200, `/api/public/documents/999999` → 404, `/sitemap.xml` 200 (XML chỉ nội dung Public đang hiện), `/openapi/v1.json` 200.
  - Bổ sung: endpoint `/sitemap.xml` (spec §5.1) trong `HomeEndpoints`.
  - Bài học (ghi `docs/decisions.md`): `dotnet watch` không có `Properties/launchSettings.json` chạy Production → bỏ qua `appsettings.Development.json` → kết nối sai; từ giờ `make api` chạy không cần biến môi trường.
- [x] M4 Bài tập (parser Word/Excel, làm bài, chấm, thống kê)
  - Verify 2026-10-08: BE 84/84 integration tests (Testcontainers Postgres 17) + `HocLieu.QuizImport.Tests` (parser docx/xlsx: 14 nhóm fixture + file thật); FE 28/28 unit tests + `typecheck`/`lint` 0 lỗi + `build` xanh — bundle chính 140.78 KB gzip (< chỉ tiêu 250 KB), mỗi trang 1 chunk riêng (React.lazy).
  - Luồng đã test (BE): import .docx/.xlsx → quiz **Hidden** + warnings (4 cách đánh dấu đáp án, phương án 1 dòng/nhiều dòng/bảng, numbering, nhóm + đoạn văn, Đúng/Sai, chọn nhiều, equation, cảnh báo thiếu đáp án/nhảy số/xung đột nguồn); tạo/sửa/rã mềm/khôi phục/nhân bản; sửa đáp án khi đã có lượt làm → 409 `quiz.has_attempts` + `affectedAttempts`, gửi lại `confirmRegrade=true` → chấm lại toàn bộ; hiện/ẩn/hẹn giờ (trước/sau khung: khách 404, trong khung làm được); tạo attempt (trộn câu/phương án, `layout` lưu server, không bao giờ trả `isCorrect`/giải thích trước khi nộp); lưu đáp án theo lô + khôi phục sau reload; hết giờ → FE tự nộp, server từ chối sau `expires_at + 30s`; `AttemptSweeper` chốt `Expired`; giới hạn lượt theo cookie `hl_dev`; chấm Single/TF/Multi (AllOrNothing + Partial), điểm thang 10 theo `score_rounding`; kết quả theo `show_answers` (Never/AfterSubmit/AfterClose); thống kê (histogram, TB/trung vị, % đúng từng câu, phân bố chọn phương án); xuất Excel kết quả + thống kê.
  - FE: `/bai-tap/:slug-:id` (giới thiệu quiz, trạng thái mở/đóng, form nhận diện, **Làm tiếp** từ localStorage), `/lam-bai/:attemptId` (mobile-first: 1 câu/màn, thẻ chạm lớn, đồng hồ theo `expiresAt` server, lưới câu, A−/A+, lưu tự động debounce 1,5s + localStorage), `/ket-qua/:attemptId` (con điểm đỏ nghiêng −4°, xem lại câu theo `show_answers`); `/gv/bai-tap` (danh sách + PublishControl + nút **Tạo từ Word/Excel/trống** + tải file mẫu), `/gv/bai-tap/:id` (6 tab: Câu hỏi (editor + nhóm + kéo-thả sắp xếp + lọc cảnh báo + "Xem như học sinh") · Cài đặt · Hiển thị (hẹn giờ theo giờ VN) · Giao cho lớp (M5) · Kết quả (bảng lượt làm + xóa lượt) · Thống kê); file mẫu `public/templates/mau-bai-tap.{docx,xlsx}`.
  - Verify prod: image `hoclieu-{api,web}:local` rebuild + `compose up -d` qua `https://hoclieu.localhost` — `/health/live`·`/health/ready` 200, SPA `/` · `/bai-tap/bai-tap-123` 200, `/api/public/home`·`taxonomy` 200, `/api/public/quizzes/999999` 404, 3 file mẫu templates 200.
  - Hướng dẫn định dạng file cho GV: [`docs/quiz-import-format.md`](docs/quiz-import-format.md).
- [x] M5 Lớp học & giao bài
  - Verify 2026-10-08: BE 96/96 integration tests (Testcontainers Postgres 17); FE 28/28 unit tests + `typecheck`/`lint` 0 lỗi + `build` xanh — bundle chính 141.10 KB gzip (< chỉ tiêu 250 KB), mỗi trang 1 chunk riêng (lop-id 19.77 kB, assignment-panel 27.17 kB/9.38 gzip, vao-lop 7.43 kB, lop 8.99 kB).
  - Luồng đã test (BE): CRUD lớp (quyền = chủ nhiệm ∨ GV bộ môn ∨ Lead/Deputy của tổ lớp ∨ Admin; tên lớp unique trong một năm học; tổ tự gán = tổ đầu của người tạo; GV chưa có tổ vẫn tạo được lớp); CRUD học sinh + nhập từ Excel (dryRun báo lỗi/trùng theo dòng, 4 dạng ngày sinh, header không dấu, không lưu file gốc) + xuất Excel; giao bài (mã 6 ký tự từ bảng 31 ký tự, khung giờ mở/đóng, `use_roster`); làm bài qua mã (quiz **Ẩn** vẫn làm được trong khung giờ — không phụ thuộc `is_live` của quiz; `use_roster` bắt buộc `studentId` thuộc lớp & active; roster chỉ lộ khi assignment đang mở và chỉ `id`+tên; giới hạn lượt theo HS/device; hết giờ tự nộp, server từ chối sau `close_at + 30s`; sweeper chốt `Expired` cho attempt InProgress của assignment đã đóng); bảng điểm (HS hoạt động × bài đã giao, ô = điểm cao nhất) + xuất Excel.
  - FE: `/gv/lop` (danh sách lớp + tạo/sửa/xóa, GV bộ môn), `/gv/lop/:id` (3 tab: **Học sinh** (nhập/xuất Excel có xem trước & báo lỗi dòng + thêm/sửa/xóa) · **Bài đã giao** (tạo assignment + mã + QR + sửa/xóa) · **Bảng điểm** (ô điểm + tô chưa làm + xuất Excel)), `/vao-lop` (nhập mã/mở QR → chọn tên trong lớp hoặc nhập tên → làm bài, "Làm tiếp" từ localStorage), tab **Giao cho lớp** trong `/gv/bai-tap/:id` (chọn lớp + giao quiz đó; picker gộp quiz của mình + quiz Public).
  - Verify prod: image `hoclieu-{api,web}:local` rebuild (publish/dist trên host → image slim) + `compose up -d` qua `https://hoclieu.localhost` — `/health/live`·`/health/ready` 200, SPA `/` · `/vao-lop` 200, `/api/public/taxonomy` 200, `/api/public/assignments/ZZZZZZ` → 404 (không lộ danh sách lớp), OpenAPI (48 KB) có `/api/teacher/classes` · `/api/public/assignments` · `gradebook` · `students/import`; log API "Đã migrate + seed database", không lỗi.
- [x] M6 Admin hoàn thiện
  - Verify 2026-10-08: BE 116/116 integration tests (Testcontainers Postgres 17); FE 28/28 unit tests + `typecheck`/`lint` 0 lỗi + `build` xanh — bundle chính 141.77 KB gzip (< chỉ tiêu 250 KB), mỗi trang admin 1 chunk riêng.
  - Luồng đã test (BE): dashboard (user theo trạng thái, hàng chờ duyệt, cảnh báo tổ chưa có tổ trưởng, nội dung mới 7/30 ngày, lượt làm bài, dung lượng file, job file lỗi); quản lý user (duyệt/từ chối kèm lý do, khóa/mở, cấp/thu quyền Admin + chặn hạ quyền admin cuối `last_admin`, chuyển quyền sở hữu nội dung, đăng xuất mọi thiết bị); nội dung (lọc theo GV/tổ/chuyên mục/thùng rác, ẩn/hiện, nổi bật, kiểm duyệt, đổi chuyên mục, xóa mềm/khôi phục); báo cáo (xử lý/bỏ qua kèm ghi chú); danh mục (chuyên mục sửa + ngưng/bật lại, môn, khối, năm học, tags); lớp + đổi GV chủ nhiệm; thông báo (Công khai/Giáo viên/Tổ) + trang tĩnh; settings (whitelist 9 key); audit log + xuất CSV; system (health DB/Cloudinary/Gotenberg, chạy lại job file lỗi); kết chuyển năm học (đặt năm hiện tại, lưu trữ lớp năm cũ, nhân bản lớp khối 1–4 lên khối +1 kèm học sinh & GV bộ môn, khối 5 chỉ lưu trữ).
  - FE: 11 trang admin mới + helpers (`features/admin`: 38 hooks) — `/admin` (stat card + hàng chờ duyệt + cảnh báo + kho file), `/admin/nguoi-dung` (bảng + duyệt/khóa/cấp quyền/chuyển nội dung/đăng xuất mọi thiết bị), `/admin/noi-dung` (tab Tài liệu/Bài tập + ẩn/hiện/nổi bật/kiểm duyệt/xóa), `/admin/bao-cao`, `/admin/danh-muc` (chuyên mục/khối/môn/năm học + tags), `/admin/lop`, `/admin/nam-hoc` (kết chuyển năm có xác nhận), `/admin/thong-bao` (thông báo + trang tĩnh), `/admin/cai-dat`, `/admin/nhat-ky` (lọc + xuất CSV), `/admin/he-thong`; kế thừa `/admin/to` · `/admin/loi-moi` của M2.
  - Verify prod: image `hoclieu-{api,web}:local` rebuild (publish/dist trên host) + `compose up -d` qua `https://hoclieu.localhost` — `/health/live` 200, SPA `/` · `/admin` 200, `/api/public/taxonomy` 200, `/api/admin/dashboard` không cookie → 401.
- [x] M7 Hoàn thiện (E2E, hiệu năng, backup, tài liệu GV)
  - Verify 2026-10-08: BE 116/116 integration tests + `HocLieu.QuizImport.Tests` 47/47 (Testcontainers Postgres 17); FE 28/28 unit tests + `typecheck`/`lint` 0 lỗi + `build` xanh — bundle chính 141.76 KB gzip (< chỉ tiêu 250 KB).
  - E2E Playwright 5/5 (`channel: chrome`, luồng thật: đăng nhập Google giả lập → GV tạo quiz từ file Word → rà soát → Hiện ngay → khách làm bài → xem kết quả). E2E bắt được 2 bug toàn prod, đã sửa: (1) React Router 7 — `:slug-:id` không match URL nào (mọi trang chi tiết khách 404) → dùng `:slugId` + parse trong trang; (2) Caddy thiếu `handle /health/*` → health trả 200 giả từ SPA fallback.
  - Bảo mật §12: verify headers trên prod qua HTTPS (HSTS, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, CSP đúng bảng §12); integration test chặn JSON làm bài công khai lộ `isCorrect`/giải thích trước khi `show_answers` cho phép.
  - Hiệu năng §13: endpoint danh sách/chi tiết 55–141 ms (chỉ tiêu p95 < 300 ms); tạo attempt < 500 ms; bundle công khai 141.76 KB gzip (< 250 KB).
  - Backup/restore drill (spec §12): stop api+backup → drop DB → nạp dump `backups/daily/hoclieu-latest.sql.gz` (pg_dump SQL gzip) vào DB trống (`PGUSER=hoclieu dropdb/createdb`) → số liệu khớp 100% (32 bảng, seed, `data_protection_keys`) → start api+backup → `/health/live`·`/health/ready`·`/api/public/taxonomy` = 200. Quy trình + mẹo Windows ghi trong [`docs/van-hanh.md`](docs/van-hanh.md) §6.
  - CI: job `openapi` chạy API không DB, so `api-types.ts` sinh lại với bản commit (`git diff --exit-code`) — lệch là fail.
  - Tài liệu: [`docs/huong-dan-giao-vien.md`](docs/huong-dan-giao-vien.md) (đăng nhập, tài liệu, bài tập, lớp & giao bài, kết quả, tổ, FAQ), [`docs/van-hanh.md`](docs/van-hanh.md) (cấu hình, cập nhật/rollback, backup/restore, giám sát, sự cố), [`docs/quiz-import-format.md`](docs/quiz-import-format.md) (định dạng file Word/Excel bài tập).

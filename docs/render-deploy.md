# Deploy hoclieu lên Render

Render thay thế cấu hình 1-VM docker compose (spec §15.1). Toàn bộ repo đã sẵn sàng qua blueprint `render.yaml` ở gốc repo.

## 1. Ánh xạ service

| Compose (1 VM)              | Render                                                          |
| --------------------------- | --------------------------------------------------------------- |
| `web` (Caddy + React dist)  | Docker service **`hoclieu-web`** — URL công khai chính của site |
| `api` (ASP.NET Core :8080)  | Docker service **`hoclieu-api`** — health check `/health/live`  |
| `db` (PostgreSQL 17)        | PostgreSQL **quản trị sẵn của Render** (**`hoclieu-db`**)       |
| `gotenberg` (Office → PDF)  | Docker service **`hoclieu-gotenberg`** — health check `/health` |
| `backup` (pg_dump hằng ngày)| Không cần — gói Postgres trả phí của Render đã có backup tự động|
| Caddy tự xin Let's Encrypt  | Render xử lý TLS ở load balancer; Caddy chạy HTTP thường trong container |

- FE gọi API **same-origin** (`/api/*`) như cũ — Caddy proxy sang `API_URL` của service api, nên FE không đổi gì.
- Migrations chạy tự động mỗi lần deploy (`Db__MigrateOnStartup=true`).
- Kiến trúc 1 instance của spec (IMemoryCache + background worker trong process) vẫn đúng với 1 instance api trên Render.

## 2. Chuẩn bị (trước khi bấm deploy)

1. **Cloudinary — bắt buộc.** Render không có ổ đĩa bền cho Docker service nên lưu file local (`Storage__LocalRoot`) không dùng được. Lấy `cloudinary://KEY:SECRET@CLOUD_NAME` tại Cloudinary → Console → Setup. Bật **"Allow delivery of PDF and ZIP files"** (Security) — spec §15.3.
2. **Google Cloud Console** → OAuth client: thêm **`https://hoclieu-web.onrender.com`** vào *Authorized JavaScript origins* (cùng client ID đang dùng dev — đã đặt sẵn trong `render.yaml` và `apps/web/Dockerfile`).
3. **Email Admin** — `Auth__AdminEmails` đã đặt là `thinhld2249@gmail.com` trong `render.yaml`; đăng nhập Google lần đầu bằng email đó sẽ thành Admin. Đổi nếu cần.

## 3. Tạo blueprint

**Cách A — dashboard (đơn giản nhất):**
1. Render → **New +** → **Blueprint** → chọn repo `ThuVienHocLieu` (branch `main`) → Render đọc `render.yaml` → **Apply**.
2. Tạo được 4 resource. Ở service `hoclieu-api` → tab **Environment** → điền giá trị thật cho **`Cloudinary__Url`** (giá trị trong blueprint là placeholder, đã đánh dấu `sync: false` nên Render không đè).
3. Bấm **Deploy** (blueprint tự deploy khi push `main`, hoặc manual mỗi lần).

**Cách B — CLI:** `render login` rồi `render blueprint create render.yaml` (sau đó vẫn phải điền `Cloudinary__Url` như trên).

**Database extensions:** migrations của app tự chạy `CREATE EXTENSION IF NOT EXISTS citext/unaccent/pg_trgm`. Nếu thấy lỗi quyền khi tạo extension, mở psql vào `hoclieu-db` và chạy 3 lệnh `CREATE EXTENSION ...` tay một lần.

## 4. Sau khi deploy

1. Mở `https://hoclieu-web.onrender.com` → trang chủ tải được.
2. `/health/ready` (qua web: `https://hoclieu-web.onrender.com/health/ready`) → Healthy (Cloudinary + Gotenberg OK).
3. Đăng nhập Google bằng email Admin → `/admin` thấy dashboard, seed đã có (chuyên mục, khối, môn, năm học).
4. Thử đủ một vòng: tải 1 file docx lên tài liệu → có preview; tạo quiz từ Word → học sinh làm qua mã giao bài.

## 5. Hạn chế & chi phí cần biết

| Hạng mục                | Ghi chú                                                                                                                                                                                                                                                                    |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Gói Free**            | Web/api/gotenberg FREE sẽ **ngủ sau 15 phút không có request** (bài tập tối thứ 6–CN sẽ "đánh thức" chậm vài chục giây); Postgres FREE là **trial 90 ngày**. Chạy thật cho trường: dùng gói trả phí (Starter) cho cả 4 resource — ước tính ~$28/tháng (giá Render hiện hành, kiểm tra lại ở dashboard). |
| **Kích thước upload**   | Load balancer Render giới hạn body request ~**10 MB** → `Upload__MaxMb=9` đã đặt sẵn trong blueprint. File to hơn tạm thời không tải được (đợi tính năng upload signed trực tiếp lên Cloudinary — backlog phase 2).                                              |
| **Gotenberg**           | Image LibreOffice nặng (~1,5 GB) và cần RAM; nếu thấy OOM/restart lặp ở service gotenberg thì nâng plan của nó lên gói RAM lớn hơn.                                                                                                                                          |
| **Domain riêng**        | Ở `hoclieu-web` → Settings → Custom Domain (vd `hoclieu.truong.edu.vn`) → chỉ định DNS theo hướng dẫn của Render → sửa env `SITE_DOMAIN=<domain>:80` và thêm domain vào Google *Authorized origins*. API/Gotenberg có thể chuyển sang **private** (tắt public) vì web proxy qua `API_URL`. |
| **Secrets**             | `Cloudinary__Url` nên nhập ở dashboard (tab Environment, đánh dấu secret) thay vì để giá trị thật trong `render.yaml`.                                                                                                                                                    |

## 6. Thay đổi code để tương thích Render (đã commit)

- `apps/web/Caddyfile`: `reverse_proxy {$API_URL:api:8080}` — mặc định `api:8080` (compose không đổi), Render set `API_URL`.
- `apps/api/Dockerfile`: entrypoint `--urls http://+:${PORT:-8080}` — Render set `$PORT`, compose mặc định 8080.
- `apps/web/Dockerfile`: build-arg `VITE_GOOGLE_CLIENT_ID` có giá trị mặc định (client ID là định danh công khai) — build Render không cần truyền build-arg.

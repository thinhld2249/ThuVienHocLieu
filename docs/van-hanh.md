# Vận hành hệ thống

Tài liệu cho người quản trị (admin) chạy và bảo trì hệ thống trên 1 VM. Chi tiết thiết kế: `qwen.md §8, §12, §13, §15` · quyết định thực thi: [`decisions.md`](decisions.md).

## 1. Kiến trúc triển khai

```
Browser ──HTTPS──► web (Caddy :443)   — React dist + reverse proxy
                       ├─ /            → file tĩnh
                       └─ /api, /sitemap.xml, /openapi → api (:8080)
api (ASP.NET Core) ──► db (PostgreSQL 17, volume pgdata)
                 ──► gotenberg (LibreOffice → PDF, mạng nội bộ, không mở port)
                 ──► Cloudinary (file gốc + preview + CDN)
backup (cron pg_dump @daily, giữ 14 ngày) ──► ./backups (+ đồng bộ ra ngoài VM)
```

1 instance, modular monolith. Background services chạy **trong** process api: `FileProcessingWorker` (xử lý preview), `AttemptSweeper` (chốt lượt làm hết hạn, 5 phút/lần), `CleanupWorker` (xóa cứng nội dung đã xóa mềm sau 30 ngày). Cache = bộ nhớ (`IMemoryCache`) — không có Redis.

Yêu cầu VM gợi ý: **2 vCPU · 4 GB RAM · 40 GB SSD** (PostgreSQL + LibreOffice + dotnet đủ chạy ~200 học sinh đồng thời lúc cao điểm).

## 2. Khởi tạo VM lần đầu

```bash
# 1. Cài Docker Engine + docker compose plugin
# 2. DNS: SITE_DOMAIN (vd hoclieu.truong.edu.vn) trỏ A record về IP công của VM
# 3. Mở cổng 80, 443 (Caddy tự xin/gia hạn chứng chỉ Let's Encrypt)

git clone <repo> /opt/hoclieu && cd /opt/hoclieu
cp deploy/.env.example deploy/.env
nano deploy/.env          # điền các giá trị mục 3
make up                   # build local + chạy (hoặc kéo image GHCR, xem mục 4)
```

Kiểm tra sau khởi động:

```bash
curl -skI https://$SITE_DOMAIN/health/live        # 200
curl -sk  https://$SITE_DOMAIN/health/ready       # Healthy / Degraded (Cloudinary chưa có thì Degraded)
docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs api | grep -i "migrate + seed"
```

Log API phải có dòng **"Đã migrate + seed database"** (lần đầu: 8 chuyên mục, khối 1–5, 13 môn, năm học hiện tại, settings mặc định, trang tĩnh).

## 3. Cấu hình

Tất cả qua biến môi trường trong `deploy/.env` (mẫu: `deploy/.env.example`). Không có secret nào trong repo.

| Nhóm     | Biến                         | Ghi chú                                                                     |
| -------- | ---------------------------- | --------------------------------------------------------------------------- |
| Domain   | `SITE_DOMAIN`                | Caddy tự HTTPS; phải khớp DNS                                               |
|          | `VITE_GOOGLE_CLIENT_ID`      | build-time của FE — **đổi phải rebuild image web**                          |
|          | `GHCR_ORG`, `TAG`            | tổ GitHub + tag image (trống = build local)                                 |
| DB       | `DB_PASSWORD`                | sinh chuỗi ngẫu nhiên 32+ ký tự                                             |
|          | `ConnectionStrings__Default` | `Host=db;…` (khối lượng nhỏ, giữ mặc định)                                  |
|          | `Db__MigrateOnStartup`       | `true` — migrate khi api khởi động                                          |
| Auth     | `Auth__GoogleClientId`       | cùng giá trị với `VITE_GOOGLE_CLIENT_ID`                                    |
|          | `Auth__AdminEmails`          | email admin khởi tạo, phân tách dấu phẩy — **login lần đầu là Admin**       |
|          | `Auth__AllowedDomains`       | trống = mọi tài khoản Google; ví dụ `truong.edu.vn` để giới hạn             |
| File     | `Cloudinary__Url`            | `cloudinary://KEY:SECRET@CLOUD` — trống = lưu đĩa local (volume `filedata`) |
|          | `Cloudinary__Root`           | `hoclieu-prod` / `hoclieu-dev` — tách môi trường                            |
|          | `Gotenberg__Url`             | giữ `http://gotenberg:3000`                                                 |
|          | `Upload__MaxMb`              | ≤ 500; phải **≤** `request_body max_size` trong Caddyfile                   |
| Security | `Security__IpHashSalt`       | chuỗi ngẫu nhiên 32 byte (băm IP)                                           |
| SMTP     | `Smtp__*`                    | tùy chọn — trống tắt email, thông báo in-app vẫn hoạt động                  |

### Google Cloud Console

1. Tạo **OAuth Client ID** loại _Web application_.
2. _Authorized JavaScript origins_: `https://{SITE_DOMAIN}` (dev: thêm `http://localhost:5173`).
3. Consent screen: scopes `openid email profile`, chuyển **In production** (chế độ _Testing_ chỉ 10 user test đăng nhập được).

### Cloudinary

1. Settings → Security → bật **"Allow delivery of PDF and ZIP files"** (tài khoản mới mặc định chặn → preview PDF lỗi 401).
2. Xem gói đang dùng: giới hạn dung lượng/file/credit — đặt `Upload__MaxMb` phù hợp (gói miễn phí rất nhỏ, không đủ cho tài liệu Office).

### Caddy (HTTPS)

Caddy tự xin chứng chỉ Let's Encrypt qua HTTP-01 (cần port 80 mở). HSTS + CSP + các header bảo mật khai trong `apps/web/Caddyfile` (spec §12). Caddy data (cert) nằm volume `caddy_data` — restart không mất.

## 4. Cập nhật / roll back

CI (GitHub Actions) build & push image `hoclieu-{api,web}:<sha>` + `latest` lên GHCR. Deploy thủ công trên VM:

```bash
cd /opt/hoclieu
# cập nhật code
git pull
# kéo image đúng tag (CI đã push) và chạy lại
TAG=<git-sha> docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
# xác minh
curl -skI https://$SITE_DOMAIN/health/live && curl -sk https://$SITE_DOMAIN/health/ready
docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs --tail 50 api web
```

- Migration EF chạy tự động khi api khởi động (`Db__MigrateOnStartup=true`) — migration thêm cột có default/an toàn cho dữ liệu có sẵn.
- **Roll back**: đặt `TAG=<sha-cũ>` → `up -d` lại. Lưu ý: migration không tự revert — nếu bản mới thêm cột, bản cũ vẫn chạy (cột thừa không gây ảnh hưởng), trừ khi bản mới đổi ngữ nghĩa cột (không làm trong dự án này — mọi thay đổi schema phải backward-compatible).
- FE có `VITE_GOOGLE_CLIENT_ID` build-time: đổi giá trị này phải `--build` image web lại.

## 5. Sao lưu (backup)

- Service `backup` (image `prodrigestivill/postgres-backup-local`): `pg_dump` (SQL thuần, nén gzip) **hằng ngày** (`SCHEDULE=@daily`), giữ **14 bản** (`BACKUP_KEEP_DAYS=14`), ghi vào các thư mục con của `deploy/backups/`:
  - `last/hoclieu-YYYYMMDD-HHMMSS.sql.gz` + symlink `hoclieu-latest.sql.gz` (bản gần nhất)
  - `daily/` (bản hằng ngày + symlink `hoclieu-latest.sql.gz`) · `weekly/` · `monthly/`
- **Bắt buộc đồng bộ `deploy/backups/` ra ngoài VM** (rclone/rsync lên S3/Google Drive/NAS) — backup cùng máy với DB không chống được mất VM. Khuyến nghị: cron `rclone copy /opt/hoclieu/deploy/backups remote:hoclieu-backups/` hằng ngày sau giờ backup.
- File tài liệu/bài tập **không cần backup riêng**: nguồn chính là Cloudinary. (Chế độ local `filedata` — nếu đang chạy không có Cloudinary — thì backup cả volume `filedata`.)
- Kiểm tra nhanh backup còn sống: `ls -lh deploy/backups/daily/` phải có file `.sql.gz` mới trong 24–36 giờ.

## 6. Khôi phục (restore)

> Chạy thử (drill) một lần trước khi cần thật — mục tiêu: khôi phục xong < 30 phút.

Dump của service là **SQL thuần gzip**, không có lệnh `DROP` → cách khôi phục chuẩn là **thay DB mới rồi nạp** (đúng ngữ cảnh "máy sạch"), áp dụng cả cho máy mới lẫn in-place:

```bash
cd /opt/hoclieu/deploy
BẢN=backups/daily/hoclieu-latest.sql.gz    # hoặc file cụ thể: backups/last/hoclieu-YYYYMMDD-HHMMSS.sql.gz

# 1. Dừng api + backup (db vẫn chạy)
docker compose -f docker-compose.yml --env-file .env stop api backup

# 2. Thay database bằng bản trống rồi nạp dump
#    (image postgres:17 với POSTGRES_USER=hoclieu: không có role "postgres" —
#     phải chạy dropdb/createdb với PGUSER=hoclieu, superuser của hệ thống)
docker compose -f docker-compose.yml --env-file .env exec db \
  sh -c "PGUSER=hoclieu dropdb --if-exists hoclieu && PGUSER=hoclieu createdb -O hoclieu hoclieu"
gunzip -c $BẢN | docker compose -f docker-compose.yml --env-file .env exec -T db \
  psql -U hoclieu -d hoclieu -v ON_ERROR_STOP=1

# 3. Khởi động lại
docker compose -f docker-compose.yml --env-file .env start api backup

# 4. Xác minh
curl -skI https://$SITE_DOMAIN/health/live
curl -sk https://$SITE_DOMAIN/health/ready
docker compose -f docker-compose.yml --env-file .env logs --tail 50 api
```

Trên **máy hoàn toàn mới**: cài Docker, clone repo, tạo `.env`, `docker compose up -d db backup` (chờ db healthy), rồi chạy bước 2–4 — không cần chạy `api` trước, migration tự chạy khi api khởi động ở bước 3.

Xác minh dữ liệu:

```bash
docker compose -f docker-compose.yml --env-file .env exec db \
  psql -U hoclieu -d hoclieu -c "SELECT count(*) FROM users; SELECT count(*) FROM documents; SELECT count(*) FROM quizzes;"
# so với số liệu trong /admin (dashboard) của phiên bản cuối cùng trước sự cố
```

Drill đã chạy thành công 2026-10-08 (sandbox `hoclieu.localhost`): drop DB → nạp dump → số liệu khớp 100% (32 bảng, seed đầy đủ, `data_protection_keys` còn) → api + backup lên lại, `/health/live`, `/api/public/taxonomy` = 200.

Mẹo cho Windows (không có `gunzip`): giải nén bằng PowerShell rồi nạp file SQL,

```powershell
$src = [System.IO.File]::OpenRead('deploy\backups\daily\hoclieu-latest.sql.gz')
$gz = New-Object System.IO.Compression.GzipStream($src, [System.IO.Compression.CompressionMode]::Decompress)
$ms = New-Object System.IO.MemoryStream; $gz.CopyTo($ms); $ms.Position = 0
[System.IO.File]::WriteAllBytes('deploy\backups\daily\restore.sql', $ms.ToArray())
docker compose -f deploy\docker-compose.yml --env-file deploy\.env exec -T db `
  psql -U hoclieu -d hoclieu -v ON_ERROR_STOP=1 < deploy\backups\daily\restore.sql
```

Lưu ý:

- Khôi phục **chỉ DB**. Phiên (cookie `hl_session`) vẫn hợp lệ vì DataProtection keys lưu trong DB (bảng `data_protection_keys`) — dump đã chứa; restart container không mất phiên.
- Sau restore, kiểm tra `/admin/he-thong` (health DB/Cloudinary/Gotenberg) và hàng đợi file lỗi (chạy lại job nếu có).
- Nếu mất cả volume `pgdata` + không có backup: hệ thống cần khởi tạo lại từ seed — mất dữ liệu user; đây là lý do bắt buộc đồng bộ backup ra ngoài VM.

## 7. Giám sát & log

- **Health**: `/health/live` (pipeline sống) · `/health/ready` (DB + Cloudinary + Gotenberg) — Caddy proxy `/health/*` về API, monitoring ngoài ping `https://{SITE_DOMAIN}/health/ready` mỗi phút.
- **Log**: Serilog JSON ra stdout — `docker compose … logs -f api` (request logging + correlation id). Không log nội dung câu trả lời, không log họ tên học sinh.
- **Trong ứng dụng**: `/admin` (dashboard: hàng chờ duyệt, nội dung mới, lượt làm bài, báo cáo vi phạm, dung lượng file, job lỗi) · `/admin/he-thong` (health 3 dịch vụ + chạy lại job file lỗi).
- **Cảnh báo nên bật** (nếu có monitoring): `/health/ready` không 200 liên tục 2 lần · dung lượng disk VM > 80% · `backups/` không có file mới sau 36 giờ · log api chứa `"Failed"` nhiều (preview file lỗi).

## 8. Sự cố thường gặp

| Triệu chứng                                           | Nguyên nhân · xử lý                                                                                                                                                   |
| ----------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Preview Office (docx/pptx/xlsx) bị vỡ font tiếng Việt | Image gotenberg build không có font — rebuild `deploy/gotenberg` với `--build-arg INSTALL_FONTS=true` (mạng VM phải tới `deb.debian.org`), test lại 1 file .docx thật |
| Preview PDF lỗi 401 ở Cloudinary                      | Chưa bật "Allow delivery of PDF and ZIP files" trong Cloudinary (mục 3)                                                                                               |
| Upload bị cắt ở file lớn                              | `Upload__MaxMb` hoặc `request_body max_size` trong Caddyfile nhỏ hơn file — tăng cả hai                                                                               |
| GV báo "phiên đăng nhập bị mất" sau deploy            | Bình thường nếu DataProtection keys mất (vd tạo volume mới) — keys lưu DB nên chỉ xảy ra khi DB bị thay; restart container **không** mất phiên                        |
| API 503 sau khi `up`                                  | Chờ db healthy (`docker compose ps`); api chờ `db.condition: service_healthy` — nếu db không healthy: xem `logs db` (disk đầy là phổ biến nhất)                       |
| Disk đầy                                              | Xóa log: `docker system prune -f`; xem `backups/` (giữ 14 bản theo design, bản cũ hơn tự xóa); dung lượng file gốc nằm ở Cloudinary, không phải VM                    |
| Cloudinary hết credit/gói                             | Hệ thống Degraded ở `/health/ready`; tài liệu đã up vẫn tải được qua CDN; nâng gói hoặc tạm cho GV dừng upload                                                        |
| Muốn làm mới toàn bộ (dev)                            | `make down` + xóa volume (`docker volume rm …pgdata`) + `make up` — migrate + seed lại từ đầu                                                                         |

## 9. Bảo mật (tóm tắt)

- HTTPS bắt buộc (Caddy + HSTS); header bảo mật: `X-Content-Type-Options`, `Referrer-Policy`, CSP (chỉ `accounts.google.com` + CDN Cloudinary cho script/img/connect) — xem `apps/web/Caddyfile`.
- Cookie `hl_session`: HttpOnly · Secure · SameSite=Lax · 14 ngày sliding; thu hồi qua `security_stamp` (đổi khi khóa tài khoản/đổi vai trò/đăng xuất mọi thiết bị) — phiên cũ chết trong ≤ 60 giây.
- CSRF: mọi request ghi bắt buộc header `X-Requested-With: hoclieu`.
- Rate limit: đăng nhập 10/phút/IP, tạo attempt 20/phút/IP, lưu đáp án 120/phút/lượt, báo cáo 5/giờ/IP, import 10/phút/user.
- Dữ liệu học sinh: PII tối thiểu (họ tên, ngày sinh, giới tính); chỉ lộ qua mã giao bài **đang mở**; không lưu file Excel danh sách gốc; IP chỉ lưu dạng hash; xuất dữ liệu HS ghi audit log. Tuân thủ Nghị định 13/2023/NĐ-CP — trang _Chính sách dữ liệu_ do Admin soạn ở `/admin/thong-bao`.
- Secret chỉ qua `.env` (đã `.gitignore`); `.env` trên VM quyền đọc chỉ root/user vận hành (`chmod 600`).
- Nhật ký hệ thống (`/admin/nhat-ky`): đăng nhập, duyệt/từ chối, đổi vai trò, ẩn/hiện, xóa, xuất kết quả, đổi cài đặt — xuất CSV khi cần kiểm toán.

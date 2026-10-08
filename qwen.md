# HỌC LIỆU — Đặc tả hệ thống cho Coding Agent

> **Mục tiêu:** Xây cổng học liệu tiểu học tối giản, lấy cảm hứng chức năng từ `tailieugiaoduc.edu.vn` (chỉ tham khảo cấu trúc & luồng; **không sao chép** nội dung, logo, hình ảnh). Quy mô ~500 tài khoản giáo viên; học sinh/phụ huynh truy cập không cần tài khoản.
>
> **Stack bắt buộc:** React (FE) · C# ASP.NET Core + PostgreSQL (BE) · Đăng nhập Google · Lưu file Cloudinary.
>
> Codename/repo: `hoclieu` · Namespace C#: `HocLieu`

---

## 0. Quy tắc làm việc cho agent

1. Đọc hết file trước khi viết code. Làm tuần tự theo milestone ở **§16**; mỗi milestone phải: build xanh, test pass, chạy được bằng `docker compose up`, cập nhật `README.md`.
2. Điểm nào spec chưa rõ: chọn phương án **đơn giản nhất đủ cho 500 user**, ghi 3–5 dòng vào `docs/decisions.md` (ngày · vấn đề · quyết định · lý do). Không dừng lại để hỏi.
3. Không thêm hạ tầng ngoài §8 (không Redis, message broker, microservice, Kubernetes).
4. Giao diện 100% tiếng Việt; code, tên bảng/cột, route API bằng tiếng Anh; route FE tiếng Việt không dấu.
5. Không dùng thư viện đã chuyển sang giấy phép thương mại: **MediatR, AutoMapper, FluentAssertions ≥ 8**. Dùng service class, mapping thủ công, Shouldly.
6. Mọi kiểm tra quyền thực hiện ở backend; frontend chỉ ẩn/hiện nút.

---

## 1. Tổng quan

### 1.1 Từ site gốc → hệ thống mới (tối giản)

| Site gốc                                                            | Hệ thống mới                                                                             |
| ------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Menu theo Lớp 1…5, mỗi lớp ~20+ "loại tài liệu" không thống nhất    | Hai trục lọc cố định: **Khối (1–5) × Chuyên mục (7)**; phụ: Môn học, Tuần, Năm học       |
| Bài tập cuối tuần = bài viết dài + link sang nền tảng thi bên ngoài | **Quiz trắc nghiệm tích hợp**, tạo tự động từ file Word/Excel, mặc định ẩn, hẹn giờ hiện |
| Đề thi ma trận / Ôn tập                                             | **Đề khảo sát** (quiz và/hoặc file đề)                                                   |
| Sáng kiến, Biện pháp, Kinh nghiệm dạy học                           | **Chuyên đề**                                                                            |
| PPCT & lịch báo giảng, Kế hoạch giáo dục                            | **Phân phối chương trình**                                                               |
| Hồ sơ tổ (công khai)                                                | **Không gian tổ nội bộ** (chỉ thành viên tổ xem)                                         |
| Link tải trỏ sang cloud drive bên ngoài                             | File lưu Cloudinary, **xem trước ngay trong trang**, tải qua link có hạn                 |
| Đăng ký tài khoản, nạp thẻ để tải                                   | Bỏ. Khách xem miễn phí; GV đăng nhập Google và được duyệt                                |
| Diễn đàn, quảng cáo, liên kết web, bộ đếm online                    | Bỏ                                                                                       |
| Lưới thẻ (ảnh + tiêu đề) + phân trang + sidebar loại tài liệu       | Giữ: lưới thẻ (ảnh trang 1 + tiêu đề + nhãn) + phân trang + bộ lọc                       |
| Trang chi tiết: ảnh bìa + bài viết SEO dài + nút tải                | Preview từng trang + mô tả ngắn + metadata + tài liệu liên quan                          |

**Thêm mới:** vai trò Tổ trưởng/Tổ phó; mời & duyệt tài khoản; quản lý lớp + danh sách học sinh từ Excel; giao bài cho lớp bằng mã/QR; bảng điểm; thống kê câu hỏi; kết chuyển năm học; nhật ký hệ thống.

### 1.2 Giả định

- 1 đơn vị triển khai (1 trường hoặc cụm trường), 1 instance. Đa trường → phase 2.
- Khối 1–5 (bảng `grades` mở rộng được).
- Học sinh/phụ huynh **không có tài khoản**; làm bài với tư cách khách (nhập tên, hoặc chọn tên trong lớp qua mã giao bài).
- Đăng nhập **chỉ bằng Google** (Gmail cá nhân hoặc Google Workspace của trường).
- Hiển thị giờ `Asia/Ho_Chi_Minh`; DB lưu UTC (`timestamptz`).
- Cao điểm: tối thứ 6 → Chủ nhật (bài tập cuối tuần), ~200 học sinh làm bài đồng thời. Phần lớn truy cập từ điện thoại, nhiều link mở trong trình duyệt của Zalo.

---

## 2. Vai trò & phân quyền

### 2.1 Mô hình

- `users.system_role`: `Admin` | `Teacher`
- `users.status`: `Pending` | `Active` | `Suspended` | `Rejected`
- `team_members.role`: `Member` | `Deputy` (tổ phó) | `Lead` (tổ trưởng) — mỗi tổ tối đa 1 `Lead`.

Tổ trưởng **không** phải system role mà là vai trò trong tổ: một GV có thể là tổ trưởng tổ A và thành viên tổ B. Chỉ Admin bổ nhiệm/miễn nhiệm Tổ trưởng.

### 2.2 Ma trận quyền

| Chức năng                                                                   |         Khách         | GV chờ duyệt | Giáo viên | Tổ phó  | Tổ trưởng |        Admin        |
| --------------------------------------------------------------------------- | :-------------------: | :----------: | :-------: | :-----: | :-------: | :-----------------: |
| Xem/tìm nội dung công khai đang hiện; làm bài                               |           ✓           |      ✓       |     ✓     |    ✓    |     ✓     |          ✓          |
| Tải file                                                                    | nếu tài liệu cho phép |      ✓       |     ✓     |    ✓    |     ✓     |          ✓          |
| Xem nội dung phạm vi "Giáo viên"                                            |                       |              |     ✓     |    ✓    |     ✓     |          ✓          |
| Xem nội dung phạm vi "Tổ"                                                   |                       |              |  tổ mình  | tổ mình |  tổ mình  |          ✓          |
| Nạp/sửa/xóa nội dung của mình; ẩn/hiện/hẹn giờ                              |                       |              |     ✓     |    ✓    |     ✓     |          ✓          |
| Quản lý lớp, học sinh, giao bài, xem kết quả lớp mình                       |                       |              |     ✓     |    ✓    |     ✓     |          ✓          |
| Ẩn/hiện nội dung của thành viên trong tổ (không xóa)                        |                       |              |           |    ✓    |     ✓     |          ✓          |
| Duyệt/từ chối GV xin vào tổ                                                 |                       |              |           |    ✓    |     ✓     |       mọi tổ        |
| Mời GV vào tổ (vai trò Member)                                              |                       |              |           |    ✓    |     ✓     | mọi tổ, mọi vai trò |
| Đặt/bỏ tổ phó, gỡ thành viên khỏi tổ                                        |                       |              |           |         |     ✓     |          ✓          |
| Tạo/sửa tổ, bổ nhiệm tổ trưởng                                              |                       |              |           |         |           |          ✓          |
| Khóa tài khoản, cấp quyền Admin, chuyển quyền sở hữu nội dung               |                       |              |           |         |           |          ✓          |
| Danh mục, cài đặt, kiểm duyệt, báo cáo vi phạm, nhật ký, kết chuyển năm học |                       |              |           |         |           |          ✓          |

GV chờ duyệt đăng nhập được nhưng chỉ thấy trang `/cho-duyet` + khu công khai.

### 2.3 Triển khai phân quyền (ASP.NET Core)

- Policy: `ActiveTeacher` (status = Active), `Admin`.
- Resource-based `IAuthorizationHandler`:
  - `CanEditContent(item)`: owner ∨ Admin. Lead|Deputy của `item.team_id` chỉ được ẩn/hiện.
  - `CanManageTeam(teamId)`: Lead|Deputy của tổ ∨ Admin.
  - `CanOwnTeam(teamId)`: Lead của tổ ∨ Admin.
  - `CanAccessClass(classId)`: GV chủ nhiệm ∨ GV bộ môn của lớp ∨ Lead|Deputy của tổ quản lý lớp ∨ Admin.
- Mọi truy vấn nội dung đi qua extension `IQueryable<T>.VisibleTo(viewer, now)` (§4.4).
- Không cho khóa/hạ quyền Admin cuối cùng đang Active.

---

## 3. Tài khoản & đăng nhập

### 3.1 Đăng nhập Google

1. FE hiển thị nút Google Identity Services (`@react-oauth/google` → `GoogleLogin`, nhận `credential` = ID token).
2. FE `POST /api/auth/google { idToken, inviteToken? }`.
3. BE xác thực bằng `GoogleJsonWebSignature.ValidateAsync(idToken, new() { Audience = [GoogleClientId] })`; bắt buộc `email_verified = true`. Nếu `Auth:AllowedDomains` khác rỗng → domain email phải thuộc danh sách.
4. Tìm user theo `google_sub` (lần đầu: theo email nếu đã được tạo qua lời mời) → cập nhật tên, avatar, `last_login_at`.
5. Trạng thái của user **mới**:
   - email ∈ `Auth:AdminEmails` → `Admin` + `Active`
   - có lời mời hợp lệ (qua token, hoặc email trùng một lời mời đang chờ) → `Active` + vào tổ theo lời mời
   - domain ∈ setting `auth.auto_approve_domains` → `Active` (chưa có tổ)
   - còn lại → `Pending`
6. `Suspended`/`Rejected` → `403` kèm thông báo, không cấp phiên.
7. Cấp cookie `hl_session` (HttpOnly, Secure, SameSite=Lax, 14 ngày, sliding). Claims: `uid`, `stamp`.
8. FE gọi `GET /api/me` → `{ id, email, fullName, avatarUrl, systemRole, status, teams:[{id,name,role}], requestedTeamId }`.

`Pending` → FE chuyển tới `/cho-duyet`: chọn tổ muốn tham gia, nhập họ tên đầy đủ & SĐT (tùy chọn) → `PUT /api/me`. Yêu cầu hiện trong hàng chờ của Tổ trưởng/Tổ phó tổ đó và của Admin.

**Trình duyệt trong app (Zalo/Facebook):** Google chặn đăng nhập trong webview. Trang `/dang-nhap` phát hiện user-agent in-app và hiện hướng dẫn "Mở bằng Chrome/Safari" + nút sao chép link. Học sinh làm bài không cần đăng nhập nên không bị ảnh hưởng.

### 3.2 Vòng đời tài khoản

```
Google login lần đầu ──► Pending ──(Tổ trưởng/Tổ phó/Admin duyệt)──► Active ◄──(Admin mở khóa)──┐
        │                   └──(từ chối + lý do)──► Rejected ──(Admin mở lại)──► Pending         │
        └──(AdminEmails / lời mời / domain tự duyệt)──► Active ──(Admin khóa)──► Suspended ──────┘
```

- Duyệt: `status = Active`, thêm vào tổ đã chọn với vai trò `Member`, ghi `approved_by/at`, gửi thông báo in-app (+ email nếu có SMTP). Duyệt hàng loạt được.
- Gỡ khỏi tổ ≠ khóa tài khoản. Nội dung của GV giữ `team_id` cũ để tổ vẫn quản lý được.

### 3.3 Lời mời

- Ai mời: Admin (mọi tổ, mọi vai trò kể cả `Lead`); Tổ trưởng/Tổ phó (tổ mình, vai trò `Member`).
- Nhập nhiều email (phân tách bằng xuống dòng, dấu phẩy, chấm phẩy), tối đa 100/lần, lời nhắn tùy chọn.
- Xử lý từng email:
  - đã là user `Active` → thêm thẳng vào tổ + thông báo;
  - là user `Pending` → duyệt luôn + vào tổ;
  - chưa có → tạo `invitations` (token ngẫu nhiên 32 byte, lưu SHA-256, hết hạn 7 ngày).
- Link `/moi/{token}`: gửi email nếu có SMTP; nếu không, UI hiện danh sách link + nút "Sao chép tất cả" (để dán vào Zalo).
- Chấp nhận: mở link → đăng nhập Google → BE kiểm tra email khớp lời mời (không phân biệt hoa thường) → `Active` + vào tổ. Không khớp → lỗi "Lời mời này dành cho t\*\*\*@gmail.com".
- Trạng thái: Chờ · Đã nhận · Hết hạn · Đã thu hồi. Thao tác: gửi lại (token mới, gia hạn), thu hồi.

### 3.4 Phiên & thu hồi

- `users.security_stamp` (uuid) đổi khi: khóa, đổi vai trò, gỡ khỏi tổ, "đăng xuất mọi thiết bị".
- `CookieAuthenticationEvents.OnValidatePrincipal` so `stamp` với DB (cache bộ nhớ 60 giây) → lệch thì hủy phiên.
- Lưu DataProtection keys vào PostgreSQL (`PersistKeysToDbContext`) để cookie không mất hiệu lực khi restart container.
- CSRF: cookie SameSite=Lax + mọi request ghi (POST/PUT/PATCH/DELETE) bắt buộc header `X-Requested-With: hoclieu`.

---

## 4. Mô hình nội dung

### 4.1 Chuyên mục (seed, Admin sửa được)

| slug                     | Tên                    | Loại nội dung   | Hiển thị mặc định | Ghi chú                                                          |
| ------------------------ | ---------------------- | --------------- | ----------------- | ---------------------------------------------------------------- |
| `bai-giang-dien-tu`      | Bài giảng điện tử      | Tài liệu        | Hiện              | thường .pptx, video                                              |
| `phan-phoi-chuong-trinh` | Phân phối chương trình | Tài liệu        | Hiện              | .docx/.xlsx                                                      |
| `ke-hoach-bai-day`       | Kế hoạch bài dạy       | Tài liệu        | Hiện              | .docx/.pdf                                                       |
| `bai-tap-cuoi-tuan`      | Bài tập cuối tuần      | Quiz + Tài liệu | **Ẩn**            | bắt buộc chọn Tuần                                               |
| `de-khao-sat`            | Đề khảo sát            | Quiz + Tài liệu | **Ẩn**            |                                                                  |
| `chuyen-de`              | Chuyên đề              | Tài liệu        | Hiện              | sáng kiến, biện pháp                                             |
| `ke-hoach-chu-nhiem`     | Kế hoạch chủ nhiệm     | Tài liệu        | Hiện              | theo tháng/năm, gắn lớp (tùy chọn)                               |
| `ho-so-to`               | Hồ sơ tổ               | Tài liệu        | Hiện              | `is_internal`: không hiện ở khu công khai, phạm vi mặc định = Tổ |

### 4.2 Phân loại mỗi nội dung

Chuyên mục (bắt buộc) · Khối (bắt buộc, trừ Hồ sơ tổ) · Môn học (tùy chọn) · Năm học (mặc định năm hiện tại) · Tuần 1–35 (bắt buộc với Bài tập cuối tuần) · Tags (tùy chọn) · Tổ (tự gán = tổ đầu tiên của người tạo, sửa được).

Môn học seed (GDPT 2018 tiểu học): Tiếng Việt, Toán, Tiếng Anh, Đạo đức, Tự nhiên và Xã hội, Khoa học, Lịch sử và Địa lí, Tin học, Công nghệ, Âm nhạc, Mĩ thuật, Giáo dục thể chất, Hoạt động trải nghiệm.

### 4.3 Phạm vi xem (`scope`)

| Giá trị    | Ai xem được                |
| ---------- | -------------------------- |
| `Public`   | Mọi người, kể cả khách     |
| `Teachers` | GV đã đăng nhập & Active   |
| `Team`     | Thành viên tổ của nội dung |
| `Private`  | Chỉ người tạo (+ Admin)    |

Người tạo, Tổ trưởng/Tổ phó của tổ nội dung và Admin luôn thấy nội dung bất kể trạng thái hiển thị.

### 4.4 Ẩn/hiện & hẹn giờ (áp dụng cho cả tài liệu và bài tập)

- `publish_mode`: `Hidden` | `Visible` | `Scheduled` (+ `publish_from`, `publish_until`; mỗi mốc có thể để trống).
- **Bài tập (quiz) luôn được tạo ở `Hidden`.** Tài liệu lấy mặc định theo chuyên mục (§4.1).
- Đổi trạng thái bất cứ lúc nào, có hiệu lực ngay (cache công khai tối đa 30 giây).
- Không cần job hẹn giờ: tính tại thời điểm truy vấn.

```text
is_live(now) = NOT is_deleted
  AND moderation_status = 'Approved'
  AND ( publish_mode = 'Visible'
     OR ( publish_mode = 'Scheduled'
          AND (publish_from  IS NULL OR publish_from <= now)
          AND (publish_until IS NULL OR now < publish_until) ) )
```

`VisibleTo(viewer, now)` = (`is_live` ∧ scope cho phép viewer) ∨ viewer là người tạo / Lead|Deputy của tổ nội dung / Admin. Dùng chung cho danh sách, chi tiết, tìm kiếm, liên quan, sitemap. Khách truy cập nội dung không thấy được → **404** (không phải 403).

Trạng thái hiển thị cho UI (tính toán): **Đang ẩn** · **Đang hiện** · **Sắp mở** (Scheduled, trước `from`) · **Đang mở** (trong khung) · **Đã đóng** (sau `until`).

UI điều khiển (có trong mọi danh sách nội dung của GV):

- Switch **Hiện/Ẩn** (chuyển `Visible`/`Hidden`, xóa lịch).
- Nút **Hẹn giờ** → popover chọn Từ/Đến (giờ VN), preset: "Cuối tuần này (T6 17:00 → CN 21:00)", "Từ bây giờ, 7 ngày", "Tùy chỉnh".
- Chọn nhiều → thao tác hàng loạt (Hiện, Ẩn, Hẹn giờ).
- Mọi thay đổi ghi audit log.

### 4.5 Kiểm duyệt nội dung (tùy chọn, mặc định TẮT)

Setting `content.require_review = true` → nội dung `Public` mới/sửa của GV chuyển `moderation_status = PendingReview`; Tổ trưởng/Tổ phó của tổ hoặc Admin duyệt (`Approved`) hoặc trả lại (`Rejected` + lý do).

---

## 5. Chức năng theo khu vực

### 5.1 Khu công khai (khách)

| Route                                     | Nội dung                                                                                                                                                                                                                                                                                                            |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/`                                       | Ô tìm kiếm lớn; chọn Khối (chip 1–5, nhớ lựa chọn trong localStorage); **Bài tập đang mở** của khối; 7 ô chuyên mục; **Mới cập nhật** (12 mục); thông báo ghim; số liệu nhỏ (số tài liệu, bài tập, giáo viên).                                                                                                      |
| `/khoi/:grade`                            | Lưới tài liệu + bài tập của khối; tab theo chuyên mục; lọc môn/tuần/năm học; sắp xếp Mới nhất/Xem nhiều; phân trang 24/trang.                                                                                                                                                                                       |
| `/chuyen-muc/:section`                    | Như trên, lọc theo khối. Bài tập cuối tuần nhóm theo Tuần (Tuần 31, 30, …).                                                                                                                                                                                                                                         |
| `/tai-lieu/:slug-:id`                     | Breadcrumb; tiêu đề; nhãn (chuyên mục, khối, môn, tuần, năm học); tác giả (tên GV + tổ, ẩn được bằng setting); **preview từng trang** (ảnh lazy-load, xem toàn màn hình); danh sách file + nút Tải (theo quyền); mô tả; tài liệu liên quan (cùng chuyên mục + khối); nút "Báo lỗi nội dung"; GV đăng nhập: nút Lưu. |
| `/bai-tap/:slug-:id`                      | Giới thiệu quiz: tên, khối/môn/tuần, số câu, thời gian, trạng thái (Đang mở đến…/Sắp mở/Đã đóng), form nhận diện (theo `identity_mode`), nút **Bắt đầu làm bài**; file đề in kèm (nếu có). Có bài đang làm dở trên máy → nút **Làm tiếp**.                                                                          |
| `/lam-bai/:attemptId`                     | Làm bài (§6.6).                                                                                                                                                                                                                                                                                                     |
| `/ket-qua/:attemptId`                     | Kết quả (§6.7).                                                                                                                                                                                                                                                                                                     |
| `/vao-lop`                                | Nhập mã giao bài 6 ký tự (hoặc mở `/vao-lop?ma=ABC123` từ QR) → chọn tên trong lớp → làm bài.                                                                                                                                                                                                                       |
| `/tim-kiem?q=`                            | Kết quả gộp tài liệu + bài tập, bộ lọc như trên. Tìm không dấu ("ke hoach") khớp có dấu.                                                                                                                                                                                                                            |
| `/dang-nhap`, `/moi/:token`, `/cho-duyet` | Xác thực (§3).                                                                                                                                                                                                                                                                                                      |
| `/trang/:slug`                            | Trang tĩnh do Admin soạn (Giới thiệu, Hướng dẫn, Chính sách dữ liệu).                                                                                                                                                                                                                                               |

SEO: đặt `<title>`/meta từng trang (React 19 hỗ trợ `<title>`/`<meta>` trong component); `/sitemap.xml` do API sinh (chỉ nội dung Public đang hiện).

### 5.2 Khu giáo viên `/gv` (Active)

| Route                                  | Nội dung                                                                                                                                                                                                           |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `/gv`                                  | Tổng quan: số tài liệu/bài tập của tôi, lượt làm bài 7 ngày, bài tập đang mở, thông báo, nút tạo nhanh.                                                                                                            |
| `/gv/tai-lieu`                         | Bảng tài liệu của tôi: lọc, tìm, cột trạng thái hiển thị + `PublishControl` (§4.4), thao tác: sửa, nhân bản, xóa (xóa mềm, khôi phục trong 30 ngày).                                                               |
| `/gv/tai-lieu/moi`, `/gv/tai-lieu/:id` | Form: tiêu đề, chuyên mục, khối, môn, năm học, tuần, phạm vi, tags, mô tả (rich text tối giản), ảnh bìa (tùy chọn), **kéo-thả nhiều file** (≤ 10, thanh tiến trình, trạng thái xử lý preview), cho phép khách tải. |
| `/gv/bai-tap`                          | Danh sách quiz: trạng thái + `PublishControl`, số lượt làm, điểm TB. Nút **Tạo từ Word**, **Tạo từ Excel**, **Tạo trống**, tải file mẫu.                                                                           |
| `/gv/bai-tap/nhap`                     | Upload file → tạo quiz Ẩn → chuyển sang màn hình rà soát (§6.5).                                                                                                                                                   |
| `/gv/bai-tap/:id`                      | Tabs: Câu hỏi · Cài đặt · Hiển thị · Giao cho lớp · Kết quả · Thống kê.                                                                                                                                            |
| `/gv/lop`, `/gv/lop/:id`               | Lớp của tôi (§7). Tabs lớp: Học sinh · Bài đã giao · Bảng điểm.                                                                                                                                                    |
| `/gv/yeu-thich`                        | Tài liệu/bài tập đã lưu.                                                                                                                                                                                           |
| `/gv/to/:teamId`                       | Không gian tổ: Hồ sơ tổ (nội dung phạm vi Tổ), thành viên, thông báo tổ; Lead/Deputy thấy thêm tab Quản trị tổ (§5.3).                                                                                             |
| `/gv/ho-so`                            | Họ tên, SĐT, tổ (ảnh lấy từ Google), "Đăng xuất mọi thiết bị".                                                                                                                                                     |

GV được dùng bài tập Public của đồng nghiệp để giao cho lớp mình, hoặc **nhân bản** về làm bản của mình.

### 5.3 Quản trị tổ (Tổ trưởng/Tổ phó) — tab trong `/gv/to/:teamId`

- **Chờ duyệt:** GV xin vào tổ (tên, email, ảnh Google, thời điểm) → Duyệt / Từ chối (lý do); duyệt hàng loạt.
- **Lời mời:** mời nhiều email, xem trạng thái, gửi lại, thu hồi, sao chép link.
- **Thành viên:** danh sách, vai trò, số nội dung. Tổ trưởng: đặt/bỏ Tổ phó, gỡ khỏi tổ.
- **Nội dung của tổ:** mọi tài liệu/bài tập có `team_id` = tổ (kể cả đang ẩn); được ẩn/hiện; duyệt nội dung nếu bật kiểm duyệt.
- **Thống kê tổ:** số nội dung theo thành viên × chuyên mục, lượt làm bài theo tuần.
- **Thông báo tổ:** đăng thông báo chỉ thành viên tổ thấy.

### 5.4 Khu Admin `/admin`

| Route               | Chức năng                                                                                                                                                                                                                                |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/admin`            | Dashboard: user theo trạng thái, **hàng chờ duyệt**, **tổ chưa có tổ trưởng** (cảnh báo), nội dung mới 7/30 ngày, lượt làm bài, báo cáo vi phạm đang mở, dung lượng Cloudinary đã dùng, job xử lý file lỗi.                              |
| `/admin/nguoi-dung` | Bảng user: lọc trạng thái/vai trò/tổ, tìm. Thao tác: duyệt/từ chối, khóa/mở khóa, cấp/thu quyền Admin, gán tổ, xem nội dung của user, **chuyển quyền sở hữu nội dung** sang GV khác (khi GV nghỉ/chuyển trường), đăng xuất mọi thiết bị. |
| `/admin/to`         | CRUD tổ (tên, mô tả, khối phụ trách), **bổ nhiệm Tổ trưởng** (chọn GV Active; người cũ thành Member), đặt Tổ phó, thêm/bớt thành viên, ngưng hoạt động tổ.                                                                               |
| `/admin/loi-moi`    | Mọi lời mời; mời vào bất kỳ tổ/vai trò nào (kể cả mời làm Tổ trưởng).                                                                                                                                                                    |
| `/admin/noi-dung`   | Tất cả tài liệu & bài tập (kể cả Private, gắn nhãn): lọc theo GV/tổ/chuyên mục/trạng thái; ẩn, xóa mềm/khôi phục, **ghim nổi bật trang chủ**, đổi chuyên mục, hàng đợi kiểm duyệt.                                                       |
| `/admin/bao-cao`    | Báo lỗi/vi phạm từ người xem: xem nội dung, xử lý/bỏ qua, ghi chú.                                                                                                                                                                       |
| `/admin/danh-muc`   | Chuyên mục (tên, icon, màu, thứ tự, hiển thị mặc định, bắt buộc tuần, bật/tắt), Khối, Môn học, Năm học (đặt năm hiện tại), Tags.                                                                                                         |
| `/admin/lop`        | Mọi lớp; đổi GV chủ nhiệm.                                                                                                                                                                                                               |
| `/admin/nam-hoc`    | **Kết chuyển năm học:** tạo năm mới, đặt hiện tại, lưu trữ lớp năm cũ, tùy chọn nhân bản lớp lên khối +1 kèm học sinh (khối 5 chỉ lưu trữ).                                                                                              |
| `/admin/thong-bao`  | Thông báo (Công khai / Giáo viên / Tổ), banner trang chủ, trang tĩnh.                                                                                                                                                                    |
| `/admin/cai-dat`    | Tên site, logo, liên hệ; domain tự duyệt; bật kiểm duyệt; dung lượng tối đa/file; định dạng cho phép; mặc định "khách được tải"; hiện tên tác giả công khai.                                                                             |
| `/admin/nhat-ky`    | Audit log: lọc theo người/hành động/thời gian, xuất CSV.                                                                                                                                                                                 |
| `/admin/he-thong`   | Hàng đợi xử lý file (chạy lại job lỗi), health DB/Cloudinary/Gotenberg, phiên bản app.                                                                                                                                                   |

Settings (`app_settings`, key → jsonb):

| key                                                | mặc định                                                                         |
| -------------------------------------------------- | -------------------------------------------------------------------------------- |
| `site.name` / `site.logo_file_id` / `site.contact` | "Học Liệu" / null / `{}`                                                         |
| `auth.auto_approve_domains`                        | `[]`                                                                             |
| `content.require_review`                           | `false`                                                                          |
| `content.show_author_public`                       | `true`                                                                           |
| `download.guest_default`                           | `false` (giá trị mặc định của ô "Cho phép khách tải" khi tạo tài liệu)           |
| `upload.max_mb`                                    | `50`                                                                             |
| `upload.allowed_ext`                               | `["pdf","doc","docx","ppt","pptx","xls","xlsx","jpg","jpeg","png","webp","mp4"]` |

---

## 6. Bài tập trắc nghiệm (Quiz)

### 6.1 Vòng đời

```
Tạo (Word / Excel / trống) ──► Quiz ở trạng thái ẨN + cảnh báo nhận diện
   ──► GV rà soát & sửa ──► Hiện ngay | Hẹn giờ | Giao cho lớp (mã/QR)
   ──► Học sinh làm ──► Đóng (hết giờ hiển thị / GV ẩn) ──► Kết quả, thống kê, xuất Excel
```

- Sửa quiz khi đã có lượt làm: sửa chữ tự do. Đổi đáp án đúng / xóa câu / xóa phương án → API trả `409` kèm số lượt bị ảnh hưởng; FE hỏi xác nhận → gửi lại với `confirmRegrade=true` → lưu và **chấm lại** toàn bộ lượt làm.
- Nhập lại từ file mới (thay toàn bộ câu hỏi) chỉ cho phép khi chưa có lượt làm.

### 6.2 Định dạng file Word chuẩn

Viết nội dung này thành `docs/quiz-import-format.md` (hướng dẫn cho GV) và tạo file mẫu `apps/web/public/templates/mau-bai-tap.docx` (sinh bằng OpenXml từ một test helper, commit vào repo).

**Quy tắc**

1. **Câu hỏi** bắt đầu dòng mới bằng `Câu <số>` + `:` `.` hoặc `)` (không phân biệt hoa thường). Chấp nhận thêm `Bài <số>` và `<số>.` / `<số>)` ở đầu dòng.
2. **Phương án** bắt đầu bằng `A.` `A)` `a)` … đến `H`. Có thể nhiều phương án trên 1 dòng (cách nhau bằng tab hoặc ≥ 2 dấu cách), hoặc nằm trong bảng (vd bảng 2×2).
3. **Đánh dấu đáp án đúng** — 1 trong 4 cách (ưu tiên từ trên xuống):
   - a. Dấu `*` ngay trước chữ cái: `*B.`
   - b. Dòng `Đáp án: B` (nhiều đáp án: `Đáp án: A, C`) sau các phương án.
   - c. Khối đáp án cuối file sau tiêu đề `ĐÁP ÁN` / `BẢNG ĐÁP ÁN`: `1.C 2.B 3.AC` hoặc `1-C; 2-B` hoặc bảng 2 hàng (`Câu | 1 | 2 …` / `Đáp án | C | B …`).
   - d. Định dạng: phương án đúng được **gạch chân** hoặc **tô chữ đỏ**, hoặc là phương án **duy nhất** in đậm trong câu.
4. Có > 1 đáp án đúng → câu **chọn nhiều**.
5. Câu **Đúng/Sai**: phương án là `Đúng` / `Sai` (có hoặc không có chữ cái).
6. **Giải thích**: dòng bắt đầu `Giải thích:` / `Lời giải:` / `Hướng dẫn:` sau phương án/đáp án.
7. **Nhóm & đoạn văn**: dòng `PHẦN …` / `Phần …` mở nhóm mới. Văn bản/ảnh nằm giữa tiêu đề nhóm và câu hỏi đầu tiên của nhóm = đoạn dẫn (bài đọc). Dòng dạng "Đọc đoạn văn sau…/Quan sát hình…/Dựa vào… trả lời câu…" ở giữa bài cũng mở nhóm mới.
8. Văn bản trước câu hỏi đầu tiên (không thuộc nhóm nào) → mô tả của bài tập.
9. Ảnh PNG/JPG/GIF trong câu hỏi/phương án được giữ. Công thức Equation đơn giản (phân số, lũy thừa, chỉ số) → chuyển thành văn bản. Ảnh WMF/EMF (thường do MathType) → cảnh báo, GV thay bằng ảnh PNG trong editor.
10. Chỉ nhận `.docx` (file `.doc`: mở bằng Word → Lưu thành → Word Document).

**Ví dụ**

```text
BÀI TẬP CUỐI TUẦN 5 – TOÁN 5                         ← mô tả bài tập

PHẦN I. TRẮC NGHIỆM                                   ← nhóm
Câu 1: Số thập phân gồm 5 đơn vị, 7 phần mười viết là:
A. 5,07        *B. 5,7        C. 57        D. 0,57      ← nhiều phương án/1 dòng, * = đúng

Câu 2. Phân số nào bằng 3/4?
A. 6/8
B. 4/3
C. 3/8
D. 9/16
Đáp án: A
Giải thích: Nhân cả tử và mẫu của 3/4 với 2 được 6/8.

Câu 3: Những số nào lớn hơn 2,5? (chọn nhiều đáp án)
*A. 2,51
B. 2,05
*C. 3
D. 2,499

Câu 4: 0,5 = 1/2.
*Đúng
Sai

PHẦN II. ĐỌC HIỂU
Đọc đoạn văn sau và trả lời câu 5–6:                  ← đoạn dẫn của nhóm
   Mùa thu, bầu trời như cao hơn…
Câu 5: Đoạn văn tả cảnh mùa nào?
A. Mùa xuân    B. Mùa hạ    C. Mùa thu    D. Mùa đông
Câu 6: Từ nào là danh từ chung?
A. Hà Nội      B. sông      C. Hồng       D. Việt Nam

ĐÁP ÁN                                                ← khối đáp án cuối file
5.C  6.B
```

### 6.3 Thuật toán parser (`HocLieu.QuizImport`)

Thư viện thuần: input `Stream` → output `QuizDraft` + `List<ImportWarning>`. Không phụ thuộc DB/network. Dùng `DocumentFormat.OpenXml`.

1. **Mở & giới hạn:** `WordprocessingDocument.Open(stream, false)`; từ chối nếu không phải docx, > 20 MB, > 300 câu.
2. **Làm phẳng body theo thứ tự tài liệu** thành `List<Line>`. Mỗi `Paragraph` (kể cả trong ô bảng, duyệt theo hàng → ô; ghi `TableIndex/Row/Cell`) → `Line { Runs[] (Text, Bold, Italic, Underline, ColorHex, Highlight, VertAlign), Images[] (relId, contentType, bytes), Equations[], Numbering? }`. `<w:br/>` tách thành dòng logic mới nếu đoạn sau khớp mẫu câu hỏi/phương án; ngược lại giữ làm xuống dòng.
3. **Chuẩn hóa:** Unicode NFC; `\u00A0`, `\u2002–\u200B` → space; `：` → `:`; gộp khoảng trắng (giữ tab làm dấu phân cách); trim.
4. **Đánh số tự động (numbering.xml):** giữ bộ đếm theo `(numId, ilvl)`, lấy `numFmt` + `lvlText` → sinh nhãn (`upperLetter`→`A.`, `lowerLetter`→`a)`, `decimal`→`1.`) và **chèn vào đầu text** để tầng regex xử lý như văn bản gõ tay.
5. **Phân loại dòng** (regex, không phân biệt hoa thường; trong bảng dưới `\|` nghĩa là `|`):

   | Loại               | Mẫu                                                                                                                   |
   | ------------------ | --------------------------------------------------------------------------------------------------------------------- |
   | `GroupHeader`      | `^(PHẦN\|Phần)\s+([IVXLC]+\|\d+)\b`                                                                                   |
   | `GroupIntro`       | `^(Đọc\|Quan sát\|Dựa vào\|Cho)\b.*(câu\|trả lời)` (khi không ở trạng thái Stem)                                      |
   | `AnswerKeyHeader`  | `^(BẢNG\s+)?ĐÁP\s*ÁN\b` (cả dòng, không có `:` theo sau chữ cái)                                                      |
   | `QuestionStart`    | `^(Câu\|Bài)\s*(\d{1,3})\s*[:.)]?\s*(.*)$` hoặc `^(\d{1,3})\s*[.)]\s+(.*)$` (dạng sau không áp dụng trong AnswerKey)  |
   | `Option`           | `^(\*?)\s*([A-Ha-h])\s*[.)]\s*(.*)$`; tách nhiều phương án/dòng tại vị trí khớp `(?:^\|\t\|\s{2,})(\*?)([A-H])[.)]\s` |
   | `TrueFalseOption`  | `^(\*?)\s*(Đúng\|Sai)\s*\.?$`                                                                                         |
   | `AnswerLine`       | `^(Đáp\s*án\|ĐA)(\s*đúng)?\s*[:\-]\s*([A-H](\s*[,;]?\s*(và)?\s*[A-H])*\|Đúng\|Sai)\s*$`                               |
   | `ExplanationStart` | `^(Giải\s*thích\|Lời\s*giải\|Hướng\s*dẫn(\s*giải)?)\s*:`                                                              |
   | `Text`             | còn lại                                                                                                               |

6. **Máy trạng thái:**
   ```
   Preamble   --Text/Image-->        Preamble (→ mô tả bài)
   *          --GroupHeader-->       GroupIntro (nhóm mới)
   GroupIntro --Text/Image-->        GroupIntro (→ passage)
   *          --QuestionStart-->     Stem (câu mới)
   Stem       --Text/Image-->        Stem (nối vào đề)
   Stem       --Option/TF-->         Options
   Options    --Option/TF-->         Options
   Options    --Text-->              Options (nối vào phương án cuối, nếu dòng không rỗng)
   Options    --GroupIntro-->        GroupIntro (nhóm mới không tiêu đề)
   Stem|Options --AnswerLine-->      (ghi đáp án, giữ trạng thái)
   Options    --ExplanationStart-->  Explanation
   Explanation --Text/Image-->       Explanation
   *          --AnswerKeyHeader-->   AnswerKey (đến hết file)
   ```
7. **Khối đáp án:** trong `AnswerKey`, quét text bằng `(\d{1,3})\s*[.\-:)]?\s*([A-H]{1,8}|Đúng|Sai|Đ|S)\b`; với bảng: tìm hàng có ô đầu "Câu" và hàng "Đáp án" → ghép theo cột. Chấp nhận cả dòng `Câu 1: B`.
8. **Xác định đáp án đúng mỗi câu:** nguồn theo thứ tự `Asterisk > AnswerLine > AnswerKey > Formatting`; lấy nguồn đầu tiên có dữ liệu. Nguồn ưu tiên thấp hơn có dữ liệu và khác → cảnh báo `CONFLICTING_ANSWER_SOURCES`. Quy tắc Formatting: phương án đúng nếu **toàn bộ run nội dung** (trừ nhãn `A.`) gạch chân, hoặc màu đỏ (R ≥ 180, G ≤ 90, B ≤ 90), hoặc highlight; in đậm chỉ tính khi đúng **một** phương án của câu được in đậm.
9. **Loại câu:** phương án = {Đúng, Sai} → `TrueFalse`; > 1 đúng → `Multi`; còn lại `Single`.
10. **Dựng HTML:** run → `<p>` với `<strong> <em> <u> <sub> <sup>` (giữ sub/sup cho m², H₂O); ảnh → `<img data-temp-id="img-N">`; Equation (OMML): `m:f` → `a/b`, `m:sSup` → `x<sup>2</sup>`, `m:sSub` → `x<sub>1</sub>`, `m:rad` → `√(x)`; cấu trúc khác → lấy text + cảnh báo `EQUATION_SIMPLIFIED`.
11. **Ảnh:** parser trả bytes + tempId; tầng API upload lên Cloudinary (`{root}/quiz/{quizId}/img-{n}`) và thay `src`. WMF/EMF → cảnh báo `UNSUPPORTED_IMAGE_FORMAT`, chèn khung "[Ảnh chưa hiển thị được — hãy thay bằng ảnh PNG]".
12. **Kiểm tra & cảnh báo** (mỗi cảnh báo có `code`, `questionNumber?`, `message` tiếng Việt):
    `NO_CORRECT_ANSWER` · `TOO_FEW_OPTIONS` (< 2) · `EMPTY_OPTION` · `DUPLICATE_OPTION_LABEL` · `QUESTION_NUMBER_GAP` · `DUPLICATE_QUESTION_NUMBER` · `ANSWER_KEY_UNMATCHED` · `CONFLICTING_ANSWER_SOURCES` · `UNSUPPORTED_IMAGE_FORMAT` · `EQUATION_SIMPLIFIED`.
13. **Output:**

```json
{
  "title": "Bài tập cuối tuần 5 – Toán 5",
  "descriptionHtml": "<p>…</p>",
  "groups": [
    {
      "tempId": "g1",
      "title": "PHẦN II. ĐỌC HIỂU",
      "passageHtml": "<p>Mùa thu…</p>"
    }
  ],
  "questions": [
    {
      "number": 5,
      "groupTempId": "g1",
      "type": "Single",
      "contentHtml": "<p>Đoạn văn tả cảnh mùa nào?</p>",
      "options": [
        { "label": "A", "contentHtml": "Mùa xuân", "isCorrect": false },
        { "label": "C", "contentHtml": "Mùa thu", "isCorrect": true }
      ],
      "explanationHtml": null,
      "answerSource": "AnswerKey"
    }
  ],
  "warnings": [
    {
      "code": "NO_CORRECT_ANSWER",
      "questionNumber": 7,
      "message": "Câu 7 chưa xác định được đáp án đúng"
    }
  ]
}
```

**Import vào hệ thống:** `POST /api/teacher/quizzes/import/docx` → parse → upload ảnh → **tạo quiz luôn** (Hidden, `import_warnings` lưu jsonb, `source_file_id` = file gốc) → trả `{ quizId, warnings }`. Không có bản nháp tạm → không có ảnh mồ côi; GV không muốn giữ thì xóa quiz.

**Test bắt buộc** (`HocLieu.QuizImport.Tests`, fixture sinh bằng `DocxBuilder` helper + vài file .docx thật commit trong `fixtures/`):

1. Đánh dấu `*` · 2. Dòng `Đáp án:` · 3. Khối đáp án dạng text · 4. Khối đáp án dạng bảng · 5. Gạch chân / chữ đỏ / một phương án in đậm · 6. Nhiều phương án/1 dòng (tab và dấu cách) · 7. Phương án trong bảng 2×2 · 8. Phương án đánh số tự động (numbering) · 9. Ảnh trong đề và phương án · 10. Nhóm + đoạn văn · 11. Câu Đúng/Sai · 12. Chọn nhiều · 13. Equation phân số · 14. File lỗi: thiếu đáp án, nhảy số câu, xung đột nguồn → đúng cảnh báo. File mẫu của hệ thống phải nhập đúng 100%.

### 6.4 Nhập từ Excel

File mẫu `mau-bai-tap.xlsx` (ClosedXML), sheet đầu tiên, hàng 1 là tiêu đề:

| Cột        | Bắt buộc | Ví dụ                                           |
| ---------- | :------: | ----------------------------------------------- |
| STT        |          | 1                                               |
| Câu hỏi    |    ✓     | Số thập phân gồm 5 đơn vị, 7 phần mười viết là: |
| A … F      | ≥ 2 cột  | 5,07                                            |
| Đáp án     |    ✓     | `B` · `A,C` · `Đúng`                            |
| Giải thích |          |                                                 |
| Nhóm       |          | PHẦN II. ĐỌC HIỂU                               |
| Đoạn văn   |          | (chỉ cần ở dòng đầu của nhóm)                   |
| Điểm       |          | 1                                               |

Đọc mọi ô bằng **chuỗi đã định dạng** (`GetFormattedString()`) để "5,7" không bị hiểu thành số. Kết quả dùng chung `QuizDraft` và luồng tạo quiz như Word.

### 6.5 Màn hình rà soát & editor

- Banner: "Đã nhận diện 20 câu — 2 câu cần kiểm tra" + nút lọc câu có cảnh báo.
- Desktop 2 cột: trái = danh sách câu (số, trích đề, icon ✓/⚠, kéo-thả sắp xếp); phải = editor câu đang chọn. Mobile: 1 cột.
- Editor câu: đề (Tiptap tối giản: đậm/nghiêng/gạch chân/chỉ số trên-dưới/ảnh), loại câu, phương án (thêm/xóa/sắp xếp; radio cho câu 1 đáp án, checkbox cho chọn nhiều), giải thích, điểm, nhóm. Nhóm: sửa tiêu đề & đoạn văn.
- Thao tác hàng loạt: đặt điểm cho tất cả, xóa nhiều câu.
- "Xem như học sinh" (preview đúng giao diện làm bài).
- Lưu: nút **Lưu** + cảnh báo khi rời trang có thay đổi chưa lưu. `PUT` thay toàn bộ (câu có `id` = cập nhật, không `id` = thêm, thiếu = xóa), concurrency theo `xmin` → `409` nếu người khác đã sửa.
- Tab **Cài đặt:** thời gian làm (phút, trống = không giới hạn), trộn câu, trộn phương án, hiện đáp án (`Never` / `AfterSubmit` / `AfterClose`), nhận diện (`Anonymous` / `Name` / `NameAndClass`), số lượt tối đa, chấm câu chọn nhiều (`AllOrNothing` / `Partial`), làm tròn điểm (`None` / `Quarter` / `Half` / `Integer`), file đề in kèm.
- Tab **Hiển thị:** `PublishControl` + phạm vi + hiển thị trạng thái hiện tại bằng lời ("Đang mở đến 21:00 CN 11/10").

### 6.6 Làm bài (khách, mobile-first)

- **Bắt đầu:** `POST /api/public/quizzes/{id}/attempts` (quiz phải `is_live` + scope Public) hoặc `POST /api/public/assignments/{code}/attempts` (assignment đang mở; **không phụ thuộc** trạng thái ẩn/hiện của quiz → GV có thể giao bài đang ẩn chỉ cho lớp mình). Server tạo attempt, trộn & lưu `layout`, đặt `expires_at` (nếu có giới hạn giờ), trả câu hỏi **không có** `isCorrect`/giải thích. Nhãn A/B/C/D do FE gán theo vị trí hiển thị sau trộn.
- **Giao diện:** 1 câu/màn hình, phương án là thẻ lớn (cao ≥ 56px, chạm toàn thẻ), thanh tiến trình, lưới câu (đã làm / chưa làm / đánh dấu xem lại), đồng hồ đếm ngược theo `expiresAt` của server, nút A−/A+ cỡ chữ, đoạn văn của nhóm hiển thị thu gọn được phía trên câu hỏi. Desktop có tùy chọn "Hiện tất cả câu".
- **Lưu tự động:** mỗi thay đổi → debounce 1,5s → `PUT /answers` (gửi theo lô); đồng thời lưu `localStorage[attemptId]` để khôi phục khi tải lại/mất mạng; chỉ báo "Đã lưu"; retry có backoff. `localStorage['hl_attempts']` lưu danh sách attempt đang dở để hiện nút **Làm tiếp**.
- **Nộp bài:** hộp xác nhận nêu số câu chưa làm → `POST /submit` → chuyển `/ket-qua/:id`. Hết giờ → FE tự nộp; server từ chối ghi sau `expires_at + 30s`.
- **Chốt bài bỏ dở:** `AttemptSweeper` (BackgroundService, 5 phút/lần) chấm và chuyển `Expired` các attempt quá `expires_at + 30s`, và các attempt `InProgress` của quiz/assignment đã đóng.
- **Giới hạn lượt:** theo `student_id` (khi chọn tên trong lớp) hoặc theo cookie thiết bị `hl_dev` (uuid ngẫu nhiên, 1 năm) — giới hạn mềm.

### 6.7 Chấm điểm & kết quả

- Chấm **chỉ ở server**. `Single`/`TrueFalse`: đúng khi tập chọn = tập đúng. `Multi`: `AllOrNothing` (mặc định) hoặc `Partial` = `max(0, (số đúng đã chọn − số sai đã chọn) / số đáp án đúng) × điểm`.
- `score_10 = round(score / total_points × 10)` theo `score_rounding`. Lưu `is_correct`, `points_awarded` từng câu.
- Trang kết quả: điểm thang 10, số câu đúng/tổng, thời gian làm. Theo `show_answers`: `Never` → chỉ điểm; `AfterSubmit` → xem lại từng câu (đã chọn, đáp án đúng, giải thích); `AfterClose` → chỉ điểm cho tới khi quiz/assignment đóng, sau đó xem lại được. Nút "Làm lại" nếu còn lượt.

### 6.8 Kết quả & thống kê cho GV

- **Bảng lượt làm:** Họ tên · Lớp · Điểm (10) · Số câu đúng · Thời gian làm · Nộp lúc · Lượt thứ; lọc theo lớp/assignment; chọn hiển thị Lượt cao nhất / đầu / cuối; xóa lượt rác.
- **Thống kê:** phân bố điểm (histogram), điểm TB/trung vị, **% đúng từng câu**, phân bố chọn phương án; câu < 30% đúng được tô nổi (gợi ý đề/đáp án có thể sai).
- **Xuất Excel:** sheet Kết quả + sheet Thống kê câu (ClosedXML). Ghi audit log khi xuất.

---

## 7. Lớp học & học sinh

- **Lớp:** tên (5A), khối, năm học, tổ, GV chủ nhiệm (người tạo), GV bộ môn (`class_teachers`, kèm môn). Tên lớp duy nhất trong một năm học.
- **Học sinh:** nhập từ Excel mẫu `mau-danh-sach-hs.xlsx` (STT · Họ và tên\* · Ngày sinh · Giới tính · Mã HS) → **xem trước & báo lỗi từng dòng** (`dryRun=true`) → xác nhận ghi. Trùng (họ tên + ngày sinh) → bỏ qua và báo. Thêm/sửa/sắp xếp/ngưng hoạt động thủ công; xuất Excel.
- **Không lưu file Excel gốc** của danh sách học sinh (đọc xong bỏ) — giảm rủi ro dữ liệu cá nhân.
- **Giao bài:** chọn quiz (của mình hoặc quiz Public của đồng nghiệp) → tạo assignment: mã 6 ký tự (bảng chữ không gồm 0/O/1/I/L), giờ mở/đóng, `use_roster` (chọn tên trong danh sách lớp) hoặc nhập tên tự do. Hiện **link + mã QR** (`qrcode.react`) + nút sao chép để gửi nhóm Zalo phụ huynh.
- **Chế độ danh sách lớp:** `/vao-lop?ma=…` → hiện tên lớp + danh sách họ tên → học sinh chọn tên mình → làm bài (`attempt.student_id`). API chỉ trả `id` + `full_name` và chỉ khi assignment đang mở.
- **Bảng điểm lớp:** hàng = học sinh, cột = bài đã giao, ô = điểm (lượt cao nhất); ô trống = chưa làm (tô nổi); xuất Excel.
- **Kế hoạch chủ nhiệm** có thể gắn `class_id` để hiện trong trang lớp.
- Xóa học sinh/lớp → xóa cứng luôn lượt làm & câu trả lời liên quan (sau xác nhận).

---

## 8. Kiến trúc

### 8.1 Sơ đồ triển khai

```
┌──────────────────────────────── 1 VM · Docker Compose ─────────────────────────────────┐
│                                                                                         │
│  Browser ──HTTPS──► web (Caddy :443)                                                    │
│  (React SPA)          ├── /            → file tĩnh React (dist)                          │
│                       └── /api/*, /sitemap.xml → api (ASP.NET Core :8080)               │
│                                                  ├──► db (PostgreSQL 17, volume pgdata) │
│                                                  ├──► gotenberg (LibreOffice → PDF)     │
│                                                  └── BackgroundServices trong process:  │
│                                                       FileProcessingWorker,             │
│                                                       AttemptSweeper, CleanupWorker     │
│  backup (cron pg_dump hằng ngày, giữ 14 ngày) ──► ./backups (+ đồng bộ ra ngoài VM)      │
└─────────────────────────────────────────────────────────────────────────────────────────┘
   api ──► Cloudinary API (lưu file gốc, PDF preview, ảnh trang, thumbnail)
   api ──► Google (khóa công khai để xác thực ID token)
   Browser ──► Google Identity Services (nút đăng nhập) · Cloudinary CDN (ảnh trang qua URL ký)
```

**Vì sao:** modular monolith, 1 đơn vị deploy — đủ cho 500 user, dễ vận hành. Cache dùng `IMemoryCache` (1 instance). Việc nền dùng `Channel<T>` trong process + cột trạng thái trong DB để bền vững khi restart. Cấu hình VM gợi ý: 2 vCPU · 4 GB RAM · 40 GB SSD.

### 8.2 Tech stack

| Lớp           | Công nghệ                                                                                                                                                                                                                                              |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Frontend      | React 19 + TypeScript + Vite · React Router 7 · TanStack Query 5 · Tailwind CSS 4 + shadcn/ui · react-hook-form + zod · Tiptap · `@react-oauth/google` · `qrcode.react` · dayjs (utc, timezone, locale `vi`) · `openapi-typescript` + `openapi-fetch`  |
| Backend       | .NET 10 (LTS) · ASP.NET Core Minimal APIs · EF Core 10 + Npgsql · FluentValidation · Google.Apis.Auth · CloudinaryDotNet · DocumentFormat.OpenXml · ClosedXML · HtmlSanitizer (Ganss.Xss) · Serilog · Microsoft.AspNetCore.OpenApi (+ Scalar UI ở dev) |
| Database      | PostgreSQL 17 + extensions `citext`, `unaccent`, `pg_trgm`                                                                                                                                                                                             |
| File          | Cloudinary (lưu trữ + biến đổi + CDN) · Gotenberg 8 (chuyển Office → PDF)                                                                                                                                                                              |
| Test          | xUnit · Shouldly · Testcontainers.PostgreSql · `WebApplicationFactory` · Vitest + Testing Library · Playwright (E2E)                                                                                                                                   |
| CI/CD         | GitHub Actions → GHCR → `docker compose pull && up -d`                                                                                                                                                                                                 |
| Reverse proxy | Caddy 2 (HTTPS tự động)                                                                                                                                                                                                                                |

### 8.3 Cấu trúc monorepo

```
hoclieu/
├── apps/
│   ├── web/                                # React SPA (pnpm)
│   │   ├── public/templates/               # mau-bai-tap.docx, mau-bai-tap.xlsx, mau-danh-sach-hs.xlsx
│   │   ├── src/
│   │   │   ├── app/                        # router.tsx, providers.tsx, layouts/ (PublicLayout, TeacherLayout, AdminLayout)
│   │   │   ├── routes/                     # public/, gv/, admin/ — mỗi file 1 trang
│   │   │   ├── features/                   # auth, documents, files, quizzes, attempts, classes, teams, users, admin, search
│   │   │   │   └── <feature>/              # api.ts (query hooks), components/, schemas.ts (zod), types.ts
│   │   │   ├── components/ui/              # shadcn/ui
│   │   │   ├── components/common/          # ContentCard, PublishControl, FileDropzone, PagePreview, Pagination, EmptyState
│   │   │   ├── lib/                        # api-client.ts, api-types.ts (sinh tự động), date.ts, slug.ts, inapp-browser.ts
│   │   │   └── styles/                     # tokens.css, globals.css
│   │   ├── Dockerfile                      # node build → caddy:2 phục vụ dist + reverse proxy
│   │   └── Caddyfile
│   └── api/
│       ├── HocLieu.sln
│       ├── Directory.Build.props           # net10.0, Nullable enable, TreatWarningsAsErrors
│       ├── Directory.Packages.props        # Central Package Management
│       ├── src/
│       │   ├── HocLieu.Api/
│       │   │   ├── Program.cs
│       │   │   ├── Common/                 # Auth/, Errors/, Pagination.cs, Slugify.cs, Visibility.cs, Audit.cs, RateLimits.cs
│       │   │   ├── Domain/                 # Entities/*.cs, Enums.cs
│       │   │   ├── Infrastructure/
│       │   │   │   ├── Data/               # AppDbContext, Configurations/, Migrations/, Seed/
│       │   │   │   ├── Storage/            # IFileStorage, CloudinaryFileStorage
│       │   │   │   ├── Conversion/         # IDocumentConverter, GotenbergConverter
│       │   │   │   ├── Jobs/               # FileProcessingQueue, FileProcessingWorker, AttemptSweeper, CleanupWorker
│       │   │   │   └── Email/              # IEmailSender (SMTP; NoopEmailSender khi không cấu hình)
│       │   │   └── Features/
│       │   │       ├── Auth/ Me/ Users/ Teams/ Invitations/ Notifications/
│       │   │       ├── Taxonomy/ Documents/ Files/ Search/ Home/
│       │   │       ├── Quizzes/ Attempts/ Classes/ Assignments/
│       │   │       └── Admin/ Reports/ Announcements/ Settings/ AuditLogs/ StaticPages/
│       │   │           # mỗi feature: XxxEndpoints.cs, XxxService.cs, XxxDtos.cs, XxxValidators.cs
│       │   └── HocLieu.QuizImport/         # thư viện thuần docx/xlsx → QuizDraft
│       │       ├── Docx/                   # DocxReader, NumberingResolver, LineClassifier, QuizStateMachine, AnswerKeyParser, OmmlLinearizer
│       │       ├── Xlsx/                   # XlsxQuizReader
│       │       └── Model/                  # QuizDraft, DraftGroup, DraftQuestion, DraftOption, ImportWarning
│       ├── tests/
│       │   ├── HocLieu.QuizImport.Tests/   # fixtures/*.docx + DocxBuilder
│       │   └── HocLieu.Api.Tests/          # integration: WebApplicationFactory + Testcontainers
│       └── Dockerfile
├── deploy/
│   ├── docker-compose.yml
│   ├── gotenberg/Dockerfile                # thêm font Times New Roman/Arial
│   └── .env.example
├── docker-compose.dev.yml                  # db + gotenberg cho dev
├── docs/                                   # decisions.md, quiz-import-format.md, huong-dan-giao-vien.md, van-hanh.md
├── .github/workflows/ci.yml
├── Makefile
└── README.md
```

### 8.4 Quy ước backend

- **Vertical slice:** mỗi feature có `XxxEndpoints.cs` (`app.MapGroup("/api/...")`), `XxxService.cs` (inject `AppDbContext` trực tiếp, không repository layer), DTO record, validator.
- **Lỗi:** ProblemDetails (RFC 9457): `title` tiếng Việt cho người dùng, `code` máy đọc (vd `quiz.not_visible`, `team.not_lead`), `errors` cho validation (key camelCase).
- **Phân trang:** `?page=1&pageSize=24` (max 100) → `{ items, total, page, pageSize }`.
- **Thời gian:** inject `TimeProvider` (test được); mọi cột thời gian `timestamptz` UTC.
- **ID:** `bigint identity`; `attempts.id`, `invitations.id` dùng uuid v7 (`Guid.CreateVersion7()`).
- **Enum** lưu text (`HasConversion<string>()`).
- **Xóa mềm** nội dung bằng `is_deleted` + global query filter; `CleanupWorker` xóa cứng (DB + Cloudinary) sau 30 ngày.
- **Concurrency:** cột hệ thống `xmin` làm concurrency token (Npgsql) cho `documents`, `quizzes`.
- **Slug:** bỏ dấu, `đ→d`, `[a-z0-9-]`, tối đa 80 ký tự. URL dạng `/{slug}-{id}`; slug sai → redirect 301 về slug đúng.
- **Audit:** `IAuditLogger.LogAsync(action, entityType, entityId, data)` cho: đăng nhập, duyệt/từ chối, đổi vai trò, bổ nhiệm tổ trưởng, ẩn/hiện, xóa, xuất kết quả, đổi cài đặt.
- **Rate limit** (built-in `RateLimiter`): `/auth/google` 10/phút/IP · tạo attempt 20/phút/IP · lưu câu trả lời 120/phút/attempt · báo cáo 5/giờ/IP · import 10/phút/user.
- **OpenAPI** tại `/openapi/v1.json`; FE sinh type bằng `pnpm gen:api`.
- **Sanitize** mọi HTML do người dùng nhập trước khi lưu (§12).

### 8.5 Quy ước frontend

- Server state chỉ qua TanStack Query; trạng thái đăng nhập = query `me`.
- Guard route: `<RequireAuth />`, `<RequireActive />`, `<RequireAdmin />`; quyền tổ kiểm tra theo `me.teams`.
- Lazy-load bundle `/gv` và `/admin`; bundle công khai nhẹ (học sinh dùng điện thoại).
- Ngày giờ luôn hiển thị theo `Asia/Ho_Chi_Minh`, dạng `dd/MM/yyyy HH:mm`; input hẹn giờ nhập theo giờ VN, gửi ISO UTC.
- Form: zod schema khớp validator BE; hiển thị lỗi theo `errors` của ProblemDetails.
- Chuỗi giao diện tiếng Việt viết trực tiếp, nhất quán thuật ngữ (§14.4).

---

## 9. Data model (PostgreSQL)

> Thứ tự minh họa; EF Core migrations tự sắp xếp. Quy ước: PK `bigint GENERATED ALWAYS AS IDENTITY` (trừ ghi chú), enum lưu text, `*_at` là `timestamptz`, bảng nội dung có `created_at`, `updated_at`.

```sql
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- unaccent không IMMUTABLE → wrapper để dùng trong generated column / index
CREATE OR REPLACE FUNCTION f_unaccent(text) RETURNS text
  LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
  AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $$;

-- ===== Người dùng & tổ =====
users (
  id, email citext NOT NULL UNIQUE, google_sub text UNIQUE,
  full_name text NOT NULL, avatar_url text, phone text,
  system_role text NOT NULL DEFAULT 'Teacher',      -- Admin|Teacher
  status text NOT NULL DEFAULT 'Pending',           -- Pending|Active|Suspended|Rejected
  status_reason text,
  requested_team_id bigint REFERENCES teams,
  security_stamp uuid NOT NULL,
  approved_by bigint REFERENCES users, approved_at,
  last_login_at, created_at, updated_at
)
teams (id, name text NOT NULL UNIQUE, description text, grade_id smallint REFERENCES grades,
       is_active bool NOT NULL DEFAULT true, created_at, updated_at)
team_members (
  team_id REFERENCES teams ON DELETE CASCADE, user_id REFERENCES users ON DELETE CASCADE,
  role text NOT NULL DEFAULT 'Member',              -- Member|Deputy|Lead
  joined_at, PRIMARY KEY (team_id, user_id)
)
CREATE UNIQUE INDEX ux_team_one_lead ON team_members(team_id) WHERE role = 'Lead';

invitations (
  id uuid PRIMARY KEY, email citext NOT NULL, team_id REFERENCES teams,
  team_role text NOT NULL DEFAULT 'Member', token_hash bytea NOT NULL UNIQUE, message text,
  invited_by REFERENCES users, expires_at NOT NULL,
  accepted_at, accepted_user_id REFERENCES users, revoked_at, created_at
)
CREATE UNIQUE INDEX ux_invite_open ON invitations(email, team_id)
  WHERE accepted_at IS NULL AND revoked_at IS NULL;

-- ===== Danh mục =====
grades (id smallint PRIMARY KEY, name text NOT NULL, sort smallint NOT NULL)          -- 1..5
subjects (id, name text NOT NULL, slug text UNIQUE NOT NULL, sort smallint, is_active bool)
sections (
  id, slug text UNIQUE NOT NULL, name text NOT NULL, icon text, color text, sort smallint,
  content_kind text NOT NULL,                       -- Document|Quiz|Both
  default_publish_mode text NOT NULL,               -- Hidden|Visible
  default_scope text NOT NULL DEFAULT 'Public',
  require_week bool NOT NULL DEFAULT false,
  is_internal bool NOT NULL DEFAULT false, is_active bool NOT NULL DEFAULT true
)
school_years (id, name text UNIQUE NOT NULL, start_date date, end_date date, is_current bool NOT NULL DEFAULT false)
CREATE UNIQUE INDEX ux_one_current_year ON school_years(is_current) WHERE is_current;
tags (id, name text NOT NULL, slug text UNIQUE NOT NULL)

-- ===== File =====
files (
  id, owner_id REFERENCES users,
  original_name text NOT NULL, ext text NOT NULL, mime text NOT NULL, bytes bigint NOT NULL, sha256 text NOT NULL,
  storage_public_id text NOT NULL, storage_resource_type text NOT NULL,   -- raw|image|video
  preview_public_id text, preview_pages int, thumbnail_url text,
  processing_status text NOT NULL,                  -- Pending|Processing|Ready|Failed|NotApplicable
  processing_error text, processing_attempts smallint NOT NULL DEFAULT 0, created_at
)

-- ===== Tài liệu =====
documents (
  id, title text NOT NULL, slug text NOT NULL, summary text, description_html text,
  section_id REFERENCES sections, grade_id REFERENCES grades, subject_id REFERENCES subjects,
  school_year_id REFERENCES school_years, week_no smallint CHECK (week_no BETWEEN 1 AND 37),
  class_id REFERENCES classes,                      -- tùy chọn (vd KH chủ nhiệm của lớp)
  owner_id REFERENCES users, team_id REFERENCES teams,
  scope text NOT NULL DEFAULT 'Public',             -- Public|Teachers|Team|Private
  publish_mode text NOT NULL, publish_from, publish_until,
  moderation_status text NOT NULL DEFAULT 'Approved', moderation_note text,
  allow_guest_download bool NOT NULL DEFAULT false,
  cover_file_id REFERENCES files, is_featured bool NOT NULL DEFAULT false,
  view_count int NOT NULL DEFAULT 0, download_count int NOT NULL DEFAULT 0,
  is_deleted bool NOT NULL DEFAULT false, deleted_at,
  search_vector tsvector GENERATED ALWAYS AS (
    to_tsvector('simple', f_unaccent(lower(coalesce(title,'') || ' ' || coalesce(summary,''))))) STORED,
  created_at, updated_at
)
document_files (document_id REFERENCES documents ON DELETE CASCADE, file_id REFERENCES files, sort smallint,
                PRIMARY KEY (document_id, file_id))
document_tags  (document_id, tag_id, PRIMARY KEY (document_id, tag_id))

-- ===== Quiz =====
quizzes (
  id, title, slug, description_html,
  section_id, grade_id, subject_id, school_year_id, week_no, owner_id, team_id,
  scope text NOT NULL DEFAULT 'Public',
  publish_mode text NOT NULL DEFAULT 'Hidden', publish_from, publish_until,
  moderation_status text NOT NULL DEFAULT 'Approved',
  time_limit_minutes smallint,                       -- null = không giới hạn
  shuffle_questions bool NOT NULL DEFAULT false, shuffle_options bool NOT NULL DEFAULT false,
  show_answers text NOT NULL DEFAULT 'AfterSubmit',  -- Never|AfterSubmit|AfterClose
  identity_mode text NOT NULL DEFAULT 'Name',        -- Anonymous|Name|NameAndClass
  max_attempts smallint,                             -- null = không giới hạn
  multi_scoring text NOT NULL DEFAULT 'AllOrNothing',-- AllOrNothing|Partial
  score_rounding text NOT NULL DEFAULT 'Quarter',    -- None|Quarter|Half|Integer
  source_file_id REFERENCES files, print_file_id REFERENCES files,
  import_warnings jsonb,
  question_count int NOT NULL DEFAULT 0, total_points numeric(6,2) NOT NULL DEFAULT 0,
  attempt_count int NOT NULL DEFAULT 0,
  is_featured, is_deleted, deleted_at, search_vector (như documents), created_at, updated_at
)
question_groups (id, quiz_id REFERENCES quizzes ON DELETE CASCADE, sort smallint, title text, passage_html text)
questions (
  id, quiz_id REFERENCES quizzes ON DELETE CASCADE,
  group_id REFERENCES question_groups ON DELETE SET NULL,
  sort smallint NOT NULL, type text NOT NULL,        -- Single|Multi|TrueFalse (ShortText: phase 2)
  content_html text NOT NULL, explanation_html text,
  points numeric(5,2) NOT NULL DEFAULT 1, accepted_answers text[]
)
question_options (id, question_id REFERENCES questions ON DELETE CASCADE, sort smallint,
                  content_html text NOT NULL, is_correct bool NOT NULL)

-- ===== Lớp & làm bài =====
classes (id, name text NOT NULL, grade_id, school_year_id, team_id, homeroom_teacher_id REFERENCES users,
         is_archived bool NOT NULL DEFAULT false, created_at, updated_at, UNIQUE (school_year_id, name))
class_teachers (class_id REFERENCES classes ON DELETE CASCADE, user_id REFERENCES users, subject_id REFERENCES subjects,
                PRIMARY KEY (class_id, user_id))
students (id, class_id REFERENCES classes ON DELETE CASCADE, ordinal smallint, full_name text NOT NULL,
          student_code text, date_of_birth date, gender text, is_active bool NOT NULL DEFAULT true, created_at, updated_at)
assignments (
  id, quiz_id REFERENCES quizzes, class_id REFERENCES classes ON DELETE CASCADE,
  code char(6) NOT NULL UNIQUE, use_roster bool NOT NULL DEFAULT true,
  open_at, close_at, created_by REFERENCES users, created_at
)
attempts (
  id uuid PRIMARY KEY,                               -- uuid v7 = "chìa khóa" truy cập của khách
  quiz_id REFERENCES quizzes, assignment_id REFERENCES assignments,
  student_id REFERENCES students ON DELETE CASCADE,
  guest_name text, guest_class text, device_id text, ip_hash text,
  layout jsonb NOT NULL,                             -- thứ tự câu & phương án sau khi trộn
  status text NOT NULL,                              -- InProgress|Submitted|Expired
  started_at, expires_at, submitted_at,
  score numeric(6,2), score_10 numeric(4,2), correct_count int, question_count int, duration_sec int
)
CREATE INDEX ix_attempts_quiz ON attempts(quiz_id, submitted_at DESC);
CREATE INDEX ix_attempts_open ON attempts(status, expires_at) WHERE status = 'InProgress';
attempt_answers (
  attempt_id REFERENCES attempts ON DELETE CASCADE, question_id REFERENCES questions ON DELETE CASCADE,
  selected_option_ids bigint[] NOT NULL DEFAULT '{}', text_answer text,
  is_correct bool, points_awarded numeric(5,2), updated_at, PRIMARY KEY (attempt_id, question_id)
)

-- ===== Khác =====
favorites (user_id, item_type text, item_id bigint, created_at, PRIMARY KEY (user_id, item_type, item_id))
announcements (id, title, body_html, audience text, team_id, is_pinned bool, publish_at, expire_at, created_by, created_at)
static_pages (slug text PRIMARY KEY, title text, body_markdown text, updated_by, updated_at)
notifications (id, user_id, type text, title text, link text, read_at, created_at)
content_reports (id, item_type text, item_id bigint, reason text, detail text, reporter_user_id, reporter_ip_hash text,
                 status text NOT NULL DEFAULT 'Open', resolved_by, resolved_at, note text, created_at)
audit_logs (id, actor_user_id, action text, entity_type text, entity_id text, data jsonb, ip text, created_at)
app_settings (key text PRIMARY KEY, value jsonb NOT NULL, updated_by, updated_at)
-- + bảng DataProtectionKeys (Microsoft.AspNetCore.DataProtection.EntityFrameworkCore)

-- ===== Index chính =====
CREATE INDEX ix_docs_browse ON documents(section_id, grade_id, created_at DESC) WHERE NOT is_deleted;
CREATE INDEX ix_docs_search ON documents USING gin (search_vector);
CREATE INDEX ix_docs_title_trgm ON documents USING gin (f_unaccent(lower(title)) gin_trgm_ops);
-- tương tự cho quizzes; ix trên (owner_id), (team_id), (publish_mode, publish_from, publish_until)
```

**Tìm kiếm:** `websearch_to_tsquery('simple', f_unaccent(lower(:q)))` trên `search_vector`, xếp hạng `ts_rank` + `similarity(f_unaccent(lower(title)), :q)` để bắt lỗi gõ. Truy vấn tài liệu và quiz riêng (mỗi bên ≤ 50), gộp & sắp xếp trong bộ nhớ.

**Seed:** 8 chuyên mục (§4.1), khối 1–5, 13 môn (§4.2), năm học `2026-2027` (05/09/2026 → 31/05/2027, hiện tại), settings mặc định (§5.4), trang tĩnh rỗng `gioi-thieu`, `huong-dan`, `chinh-sach-du-lieu`.

---

## 10. API (REST, JSON, prefix `/api`)

**Auth & cá nhân**
| Method | Path | Ghi chú |
|---|---|---|
| POST | `/auth/google` | `{ idToken, inviteToken? }` → `Me` + cookie |
| POST | `/auth/logout` · `/auth/logout-all` | |
| GET / PUT | `/me` | PUT `{ fullName, phone, requestedTeamId }` |
| GET | `/me/notifications` · POST `/me/notifications/read` | |
| GET | `/invitations/{token}` | `{ emailMasked, teamName, expiresAt, status }` (công khai) |

**Công khai**
| Method | Path | Ghi chú |
|---|---|---|
| GET | `/public/home?grade=` | bài đang mở, mới cập nhật, nổi bật, thông báo, số liệu |
| GET | `/public/taxonomy` | sections, grades, subjects, schoolYears |
| GET | `/public/items?kind=document\|quiz&section=&grade=&subject=&year=&week=&q=&sort=new\|popular&page=` | danh sách gộp |
| GET | `/public/documents/{id}` · `/{id}/related` | |
| GET | `/files/{id}/pages?from=1&count=10` | URL ký ảnh từng trang (kiểm quyền theo tài liệu chứa file) |
| GET | `/files/{id}/download` | kiểm quyền → `302` tới URL tải có hạn |
| GET | `/public/quizzes/{id}` | thông tin, không có câu hỏi |
| POST | `/public/quizzes/{id}/attempts` | `{ guestName?, guestClass? }` |
| GET | `/public/assignments/{code}` | quiz + lớp + roster (`id`, `fullName`) nếu `use_roster` |
| POST | `/public/assignments/{code}/attempts` | `{ studentId? , guestName? }` |
| GET | `/public/attempts/{id}` | InProgress: câu hỏi + câu trả lời đã lưu; đã nộp: kết quả theo `show_answers` |
| PUT | `/public/attempts/{id}/answers` | `[{ questionId, optionIds[] }]` |
| POST | `/public/attempts/{id}/submit` | → kết quả |
| POST | `/public/reports` | `{ itemType, itemId, reason, detail }` |
| GET | `/public/announcements` · `/public/pages/{slug}` | |
| GET | `/sitemap.xml` (không prefix) | |

**Giáo viên** (`ActiveTeacher`)
| Method | Path | Ghi chú |
|---|---|---|
| POST | `/teacher/files` | multipart, 1 file/request → `FileDto` |
| GET | `/teacher/files/{id}` | poll trạng thái xử lý |
| GET / POST | `/teacher/documents` | |
| GET / PUT / DELETE | `/teacher/documents/{id}` | PUT có concurrency token |
| POST | `/teacher/documents/{id}/restore` · `/duplicate` | |
| PATCH | `/teacher/{documents\|quizzes}/{id}/publish` | `{ mode, from?, until? }` |
| POST | `/teacher/content/publish-bulk` | `{ items:[{kind,id}], mode, from?, until? }` |
| GET / POST | `/teacher/quizzes` | POST = tạo trống |
| POST | `/teacher/quizzes/import/docx` · `/import/xlsx` | multipart → `{ quizId, warnings }` |
| GET / PUT / DELETE | `/teacher/quizzes/{id}` | PUT thay toàn bộ; `?confirmRegrade=true` |
| POST | `/teacher/quizzes/{id}/duplicate` · `/restore` | |
| GET | `/teacher/quizzes/{id}/attempts?assignmentId=&best=true&page=` | |
| DELETE | `/teacher/quizzes/{id}/attempts/{attemptId}` | |
| GET | `/teacher/quizzes/{id}/stats` · `/export.xlsx` | |
| GET | `/templates/{mau-bai-tap.docx\|mau-bai-tap.xlsx\|mau-danh-sach-hs.xlsx}` | (hoặc file tĩnh FE) |
| GET / POST / PUT / DELETE | `/teacher/classes`, `/teacher/classes/{id}` | |
| GET / POST / PUT / DELETE | `/teacher/classes/{id}/students[/{studentId}]` | |
| POST | `/teacher/classes/{id}/students/import?dryRun=true\|false` | multipart xlsx |
| GET | `/teacher/classes/{id}/students/export.xlsx` | |
| GET / POST / PUT / DELETE | `/teacher/classes/{id}/assignments[/{assignmentId}]` | |
| GET | `/teacher/classes/{id}/gradebook` · `/gradebook.xlsx` | |
| GET / POST / DELETE | `/teacher/favorites` | |
| GET | `/teams/{teamId}/content?scope=Team` | Hồ sơ tổ cho thành viên |

**Tổ** (`CanManageTeam`; dòng có ★ cần `CanOwnTeam`)
| Method | Path | Ghi chú |
|---|---|---|
| GET | `/teams/{id}` · `/teams/{id}/members` | thành viên tổ cũng gọi được |
| PATCH ★ | `/teams/{id}/members/{userId}` | `{ role: Member\|Deputy }` |
| DELETE ★ | `/teams/{id}/members/{userId}` | |
| GET | `/teams/{id}/join-requests` | |
| POST | `/teams/{id}/join-requests/{userId}/approve` · `/reject` | reject `{ reason }`; hỗ trợ `{ userIds[] }` để duyệt hàng loạt |
| GET / POST | `/teams/{id}/invitations` | POST `{ emails[], message? }` → kết quả từng email + link |
| POST / DELETE | `/teams/{id}/invitations/{invId}/resend` · `/teams/{id}/invitations/{invId}` | |
| GET | `/teams/{id}/content` · `/teams/{id}/stats` | |
| PATCH | `/teams/{id}/content/{kind}/{itemId}/publish` · `/moderation` | |
| POST | `/teams/{id}/announcements` | |

**Admin** (`Admin`)
| Method | Path | Ghi chú |
|---|---|---|
| GET | `/admin/dashboard` | |
| GET / PATCH | `/admin/users` · `/admin/users/{id}` | PATCH `{ status, statusReason, systemRole, teamIds }` |
| POST | `/admin/users/{id}/transfer-content` · `/logout-all` | `{ toUserId }` |
| GET / POST / PUT / DELETE | `/admin/teams[/{id}]` | |
| PUT | `/admin/teams/{id}/lead` | `{ userId }` (người cũ → Member, audit, thông báo) |
| GET / POST | `/admin/invitations` | mời vào tổ/vai trò bất kỳ |
| GET / PATCH | `/admin/content?kind=&status=&ownerId=&teamId=` · `/admin/content/{kind}/{id}` | `{ isFeatured, publishMode, moderationStatus, sectionId, isDeleted }` |
| GET / PATCH | `/admin/reports[/{id}]` | |
| CRUD | `/admin/sections` · `/admin/grades` · `/admin/subjects` · `/admin/school-years` · `/admin/tags` | |
| POST | `/admin/school-years/{id}/rollover` | `{ cloneClassesToNextGrade: bool }` |
| GET | `/admin/classes` · PATCH `/admin/classes/{id}` | đổi GV chủ nhiệm |
| CRUD | `/admin/announcements` · `/admin/pages` | |
| GET / PUT | `/admin/settings` | |
| GET | `/admin/audit-logs` · `/admin/audit-logs.csv` | |
| GET | `/admin/system` · POST `/admin/system/files/{id}/retry` | |

**DTO làm bài (không bao giờ chứa đáp án trước khi được phép):**

```json
{
  "id": "0192f3a1-…",
  "status": "InProgress",
  "expiresAt": "2026-10-09T13:40:00Z",
  "quiz": {
    "title": "Bài tập cuối tuần 5 – Toán 5",
    "timeLimitMinutes": 30,
    "questionCount": 20
  },
  "groups": [
    { "id": 11, "title": "PHẦN II. ĐỌC HIỂU", "passageHtml": "<p>…</p>" }
  ],
  "questions": [
    {
      "id": 101,
      "groupId": 11,
      "number": 5,
      "type": "Single",
      "contentHtml": "<p>…</p>",
      "options": [
        { "id": 9001, "contentHtml": "Mùa xuân" },
        { "id": 9003, "contentHtml": "Mùa thu" }
      ]
    }
  ],
  "answers": [{ "questionId": 101, "optionIds": [9003] }]
}
```

---

## 11. Lưu trữ file & xem trước

### 11.1 Định dạng & giới hạn

- Cho phép theo setting `upload.allowed_ext`; tối đa `upload.max_mb` (mặc định 50 MB, đồng bộ với Caddy `request_body`). **Kiểm tra giới hạn dung lượng/file của gói Cloudinary đang dùng** (gói miễn phí giới hạn nhỏ hơn) và đặt `upload.max_mb` tương ứng.
- Kiểm tra magic bytes: `%PDF` (pdf) · `PK\x03\x04` (docx/xlsx/pptx — kiểm thêm `[Content_Types].xml`) · `D0 CF 11 E0` (doc/xls/ppt) · JPEG/PNG/WebP/MP4 header. Sai → `415`.
- Tên file gốc chỉ dùng để hiển thị; không dùng làm đường dẫn.

### 11.2 Luồng upload & xử lý

1. FE gửi multipart `POST /api/teacher/files` (1 file/request, hiển thị % qua `XMLHttpRequest.upload.onprogress`).
2. API stream xuống file tạm, kiểm tra kích thước/magic/đuôi, tính SHA-256.
3. Upload file gốc lên Cloudinary, `type = authenticated`:
   - pdf, ảnh → `resource_type = image`; docx/xlsx/pptx/doc/xls/ppt → `raw`; mp4 → `video`.
   - `public_id = {root}/files/{fileId}/{slug-ten-file}` (slug giúp tên khi tải có nghĩa).
4. Ghi `files` với `processing_status = Pending`, đẩy `fileId` vào `FileProcessingQueue` (`Channel<long>`), trả `FileDto`.
5. `FileProcessingWorker`:
   - Office → gửi Gotenberg `POST /forms/libreoffice/convert` → PDF → upload `{root}/files/{fileId}/preview` (`image`, `authenticated`) → lấy `pages` từ response.
   - PDF → preview = chính file gốc. Ảnh → preview = chính nó, `pages = 1`. Video/zip → `NotApplicable`.
   - Thumbnail: trang 1, `pg_1,w_480,c_limit,f_jpg,q_auto` → lưu bản `type = upload` tại `{root}/thumbs/{fileId}` (chỉ dùng cho nội dung Public; nội dung khác hiển thị thumbnail qua URL ký).
   - Thành công → `Ready`; lỗi → retry 3 lần (backoff 10s/60s/5m) → `Failed` + `processing_error`. Khi khởi động, đẩy lại các file `Pending`/`Processing`.
6. FE poll `GET /teacher/files/{id}` (2 giây/lần, tối đa 2 phút) để hiện trạng thái.

### 11.3 Phân phối

- **Xem trước:** `GET /files/{id}/pages?from&count` → kiểm quyền theo tài liệu chứa file → trả danh sách URL **ký** (`sign_url`) ảnh trang `pg_N,w_1100,c_limit,f_auto,q_auto`; cache 10 phút. FE hiển thị dạng cuộn dọc, lazy-load, mỗi lần 10 trang. Slide PowerPoint hiển thị ảnh tĩnh (không có hiệu ứng).
- **Tải về:** `GET /files/{id}/download` → kiểm quyền (khách chỉ khi `allow_guest_download`) → tăng `download_count` → `302` tới URL tải riêng có hạn 10 phút (`Cloudinary.DownloadPrivate(publicId, attachment: true, expiresAt, resourceType, type: "authenticated")`).
- FE **không bao giờ** upload thẳng lên Cloudinary hay biết API secret.
- Video: phát bằng `<video>` với URL ký.

### 11.4 Gotenberg

- `deploy/gotenberg/Dockerfile` kế thừa `gotenberg/gotenberg:8`, cài font Times New Roman/Arial (gói `ttf-mscorefonts-installer`, điều chỉnh theo distro của image) và chạy `fc-cache -f` — tài liệu tiếng Việt chủ yếu dùng Times New Roman, thiếu font sẽ vỡ bố cục.
- Chỉ nằm trong mạng nội bộ compose, không mở port ra ngoài; timeout 60 giây/file.
- Kiểm thử với ít nhất 3 file thật: KHBD .docx, bài giảng .pptx, PPCT .xlsx.

---

## 12. Bảo mật & dữ liệu cá nhân

- **Đáp án bí mật:** DTO công khai là các type riêng (không tái dùng entity/DTO của GV), không có `isCorrect`/`explanationHtml` cho tới khi `show_answers` cho phép. Có integration test kiểm JSON của mọi endpoint `/public/attempts/*` trước khi nộp **không chứa** các khóa này.
- **Sanitize HTML** (Ganss.Xss) cho mô tả, câu hỏi, phương án, thông báo. Allowlist: `p br strong em u s sub sup ul ol li blockquote table thead tbody tr th td a[href^=https] img[src^=https://res.cloudinary.com]`. Trang tĩnh viết Markdown, render phía FE có sanitize.
- **Dữ liệu học sinh** (họ tên, ngày sinh, giới tính): chỉ GV của lớp, Tổ trưởng/Tổ phó tổ quản lý lớp và Admin xem được. API công khai chỉ lộ `id` + `fullName` qua mã giao bài đang mở. Không lưu file danh sách gốc. Xuất danh sách/kết quả ghi audit log. Thu thập tối thiểu, tuân thủ quy định bảo vệ dữ liệu cá nhân của Việt Nam (Nghị định 13/2023/NĐ-CP và văn bản liên quan); có trang "Chính sách dữ liệu".
- **Khách làm bài:** lưu tên như nhập; IP lưu dạng `SHA-256(ip + Security:IpHashSalt)`; cookie thiết bị ngẫu nhiên, không định danh.
- **Header (Caddy):** HSTS; `X-Content-Type-Options: nosniff`; `Referrer-Policy: strict-origin-when-cross-origin`; CSP: `default-src 'self'; script-src 'self' https://accounts.google.com/gsi/client; frame-src https://accounts.google.com; connect-src 'self' https://accounts.google.com; img-src 'self' data: https://res.cloudinary.com https://lh3.googleusercontent.com; media-src https://res.cloudinary.com; style-src 'self' 'unsafe-inline' https://accounts.google.com https://fonts.googleapis.com; font-src https://fonts.gstatic.com`.
- **Secrets** chỉ qua biến môi trường; `.env` trong `.gitignore`; có `.env.example`.
- **Sao lưu:** `pg_dump` hằng ngày giữ 14 ngày + bản tuần đồng bộ ra ngoài VM; quy trình khôi phục viết trong `docs/van-hanh.md` và chạy thử 1 lần ở M7. File trên Cloudinary không cần backup riêng (nguồn chính), nhưng `CleanupWorker` chỉ xóa cứng sau 30 ngày.

---

## 13. Yêu cầu phi chức năng

| Hạng mục    | Mục tiêu                                                                                                                                                 |
| ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Hiệu năng   | p95 < 300 ms cho API danh sách/chi tiết ở 50 RPS; tạo attempt < 500 ms; bundle công khai < 250 KB gzip; Lighthouse mobile ≥ 85 cho `/` và `/bai-tap/...` |
| Dung lượng  | 500 tài khoản, 20.000 tài liệu, 200 người làm bài đồng thời, ~100.000 lượt làm/năm                                                                       |
| Khả dụng    | 1 node, mục tiêu 99%; `/health/live`, `/health/ready` (DB, Cloudinary, Gotenberg); restart không mất phiên                                               |
| Quan sát    | Serilog JSON ra stdout, request logging + correlation id; không log nội dung câu trả lời, không log họ tên học sinh                                      |
| Trình duyệt | Chrome/Edge/Safari 2 phiên bản gần nhất, **webview Zalo** (bắt buộc test luồng làm bài), Android 9+                                                      |
| Truy cập    | Điều hướng bằng bàn phím, focus rõ ràng, tương phản WCAG AA, tôn trọng `prefers-reduced-motion`, nhãn ARIA cho thẻ phương án                             |
| Ngôn ngữ    | Chỉ vi-VN; số thập phân dùng dấu phẩy khi hiển thị điểm (8,75)                                                                                           |

---

## 14. UI/UX & design tokens

### 14.1 Định hướng: "Vở ô li & mực tím"

Lấy chất liệu từ lớp học tiểu học Việt Nam: giấy vở ô li, mực tím, bút đỏ chấm bài. **Cá tính dồn vào khu công khai và trang làm bài/kết quả**; khu GV và Admin yên tĩnh, dày thông tin, ưu tiên bảng.

### 14.2 Tokens

```css
:root {
  --paper: #fafbfd; /* nền */
  --ink: #1e2233; /* chữ chính */
  --muted: #5e6577; /* chữ phụ */
  --grid: #dce4f2; /* đường kẻ ô li: họa tiết nền trang làm bài, viền thẻ, vạch chia */
  --violet: #5b3fa8; /* mực tím — màu chính: nút chính, link, focus ring */
  --redpen: #d62839; /* bút đỏ — CHỈ dùng cho con điểm ở trang kết quả */
  --correct: #1f8a5b;
  --wrong: #b4532a;
  --warn: #b7791f;
  /* màu chuyên mục (dùng làm dải màu cạnh trái thẻ + nền icon ở độ đậm 12%) */
  --sec-bai-giang: #2f6fdb;
  --sec-ppct: #0e8a87;
  --sec-khbd: #3a8d3f;
  --sec-btct: #e07a1f;
  --sec-dks: #c2375b;
  --sec-chuyen-de: #8a5a2b;
  --sec-khcn: #6b5bd6;
  --sec-ho-so-to: #5e6b7a;
}
.o-li {
  /* nền giấy ô li */
  background-color: var(--paper);
  background-image:
    linear-gradient(var(--grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--grid) 1px, transparent 1px);
  background-size: 24px 24px;
}
```

- **Font:** `Lexend` (Google Fonts, có subset Vietnamese, thiết kế cho dễ đọc — hợp học sinh tiểu học) cho toàn bộ giao diện; fallback `system-ui, "Segoe UI", Roboto, sans-serif`; `font-variant-numeric: tabular-nums` cho điểm và bảng số.
- **Thang chữ:** 14 · 16 · 18 · 22 · 28 · 36 px. Cơ sở: công khai 16px, **trang làm bài 18px**, khu GV/Admin 15px. Dòng tối đa ~72 ký tự cho mô tả.
- **Bo góc theo cấp:** thẻ phương án 14px, thẻ nội dung 10px, input/nút 8px, bảng 0. Không đổ bóng đồng loạt; thẻ dùng viền 1px `--grid`, nổi lên khi hover bằng đổi màu viền.

### 14.3 Bố cục chính

Trang chủ:

```
┌──────────────────────────────────────────────────────────────────┐
│ Học Liệu      [ Tìm tài liệu, bài tập…                  🔍 ]  Đăng nhập │
│ Khối:  (1) (2) (3) (4) [5]                                        │
├──────────────────────────────────────────────────────────────────┤
│ Bài tập đang mở · Khối 5                                          │
│ ┌──────────┐ ┌──────────┐ ┌──────────┐   (cuộn ngang trên mobile) │
│ │Tuần 5    │ │Đề KS     │ │Tuần 5    │                           │
│ │Toán      │ │giữa kì I │ │Tiếng Việt│                           │
│ │đến CN 21h│ │đến T4    │ │đến CN 21h│                           │
│ └──────────┘ └──────────┘ └──────────┘                           │
│ Chuyên mục                                                        │
│ [Bài giảng điện tử] [Phân phối CT] [KH bài dạy] [BT cuối tuần]    │
│ [Đề khảo sát] [Chuyên đề] [KH chủ nhiệm]                          │
│ Mới cập nhật                                         Xem tất cả   │
│ ┌──┐ ┌──┐ ┌──┐ ┌──┐   lưới 4 cột desktop · 2 cột mobile           │
└──────────────────────────────────────────────────────────────────┘
```

Thẻ nội dung: ảnh trang 1 (tỉ lệ A4 1 : 1,414) · dải màu chuyên mục bên trái · tiêu đề tối đa 2 dòng · nhãn khối/môn/tuần. Thẻ quiz thay ảnh bằng khối thông tin: số câu, thời gian, "Đang mở đến …".

Trang làm bài (mobile):

```
┌─────────────────────────────┐   nền .o-li
│ Tuần 5 – Toán       ⏱ 12:34 │
│ ▓▓▓▓▓▓▓░░░░░░░   Câu 6/20   │
│ ▸ Đoạn văn (chạm để mở)     │
│ Câu 6: 3/4 + 1/4 = ?        │
│ ┌─────────────────────────┐ │
│ │ (A)  1                  │ │  thẻ trắng, chạm cả thẻ;
│ └─────────────────────────┘ │  đã chọn: viền + nền tím nhạt
│ ┌─────────────────────────┐ │
│ │ (B)  4/8                │ │
│ └─────────────────────────┘ │
│ …                           │
│ [‹ Câu trước]  [Câu sau ›]  │
│ [▦ Danh sách câu]  [Nộp bài]│
└─────────────────────────────┘
```

Trang kết quả: **con điểm cỡ lớn màu `--redpen`, nghiêng −4° như điểm bút đỏ** — khoảnh khắc chuyển động duy nhất của sản phẩm (nét vẽ/scale-in 400ms một lần; `prefers-reduced-motion` → tĩnh). Bên dưới: số câu đúng, thời gian, danh sách câu ✓/✗ (nếu được xem đáp án).

Khu GV/Admin: sidebar trái (Tổng quan, Tài liệu, Bài tập, Lớp, Tổ, Yêu thích) + bảng dữ liệu; `PublishControl` là cột cố định trong mọi bảng nội dung.

### 14.4 Ngôn từ

- Động từ rõ ràng, giữ nguyên tên hành động suốt luồng: "Bắt đầu làm bài" · "Nộp bài" · "Tải về" · "Hiện" / "Ẩn" · "Hẹn giờ" · "Duyệt" / "Từ chối" · "Mời" · "Giao cho lớp". Nút "Hiện" → toast "Đã hiện bài tập".
- Lỗi nói rõ chuyện gì xảy ra và cách sửa, không xin lỗi: "File không phải .docx. Mở file bằng Word → Lưu thành → Word Document (.docx) rồi tải lên lại."
- Màn trống luôn có hành động: "Chưa có bài tập nào. Tạo từ file Word".
- Thuật ngữ thống nhất: _tài liệu_, _bài tập_, _lượt làm_, _tổ_, _tổ trưởng_, _tổ phó_, _lớp_, _giao bài_, _mã giao bài_.

---

## 15. Triển khai & cấu hình

### 15.1 `deploy/docker-compose.yml` (khung)

```yaml
services:
  web:                       # Caddy + React dist
    image: ghcr.io/<org>/hoclieu-web:${TAG:-latest}
    ports: ["80:80", "443:443"]
    environment: [SITE_DOMAIN=${SITE_DOMAIN}]
    volumes: [caddy_data:/data]
    depends_on: [api]
  api:
    image: ghcr.io/<org>/hoclieu-api:${TAG:-latest}
    env_file: .env
    depends_on: [db, gotenberg]
  db:
    image: postgres:17
    environment: [POSTGRES_DB=hoclieu, POSTGRES_USER=hoclieu, POSTGRES_PASSWORD=${DB_PASSWORD}]
    volumes: [pgdata:/var/lib/postgresql/data]
  gotenberg:
    build: ./gotenberg
  backup:
    image: prodrigestivill/postgres-backup-local
    environment: [POSTGRES_HOST=db, POSTGRES_DB=hoclieu, POSTGRES_USER=hoclieu,
                  POSTGRES_PASSWORD=${DB_PASSWORD}, SCHEDULE=@daily, BACKUP_KEEP_DAYS=14]
    volumes: [./backups:/backups]
    depends_on: [db]
volumes: { pgdata: {}, caddy_data: {} }
```

`apps/web/Caddyfile`:

```caddyfile
{$SITE_DOMAIN} {
  encode zstd gzip
  @upload path /api/teacher/files /api/teacher/quizzes/import/* /api/teacher/classes/*/students/import
  request_body @upload { max_size 60MB }
  handle /api/* { reverse_proxy api:8080 }
  handle /sitemap.xml { reverse_proxy api:8080 }
  handle {
    root * /srv
    try_files {path} /index.html
    file_server
  }
  header {
    Strict-Transport-Security "max-age=31536000"
    X-Content-Type-Options nosniff
    Referrer-Policy strict-origin-when-cross-origin
    Content-Security-Policy "<xem §12>"
  }
}
```

### 15.2 Biến môi trường (`deploy/.env.example`)

| Biến                                                 | Ví dụ                                                  | Ghi chú                                                    |
| ---------------------------------------------------- | ------------------------------------------------------ | ---------------------------------------------------------- |
| `SITE_DOMAIN`                                        | `hoclieu.truong.edu.vn`                                | Caddy                                                      |
| `DB_PASSWORD`                                        |                                                        |                                                            |
| `ConnectionStrings__Default`                         | `Host=db;Database=hoclieu;Username=hoclieu;Password=…` |                                                            |
| `Db__MigrateOnStartup`                               | `true`                                                 | 1 instance → migrate khi khởi động                         |
| `Auth__GoogleClientId`                               | `xxx.apps.googleusercontent.com`                       | FE dùng cùng giá trị (`VITE_GOOGLE_CLIENT_ID`, build-time) |
| `Auth__AdminEmails`                                  | `a@gmail.com,b@gmail.com`                              | Admin khởi tạo                                             |
| `Auth__AllowedDomains`                               | _(trống)_                                              | trống = mọi tài khoản Google                               |
| `Cloudinary__Url`                                    | `cloudinary://KEY:SECRET@CLOUD`                        |                                                            |
| `Cloudinary__Root`                                   | `hoclieu-prod`                                         | thư mục gốc, tách môi trường dev/prod                      |
| `Gotenberg__Url`                                     | `http://gotenberg:3000`                                |                                                            |
| `Upload__MaxMb`                                      | `50`                                                   | ≤ `request_body` của Caddy                                 |
| `Security__IpHashSalt`                               | chuỗi ngẫu nhiên 32 byte                               |                                                            |
| `Smtp__Host` `__Port` `__User` `__Password` `__From` |                                                        | tùy chọn                                                   |

### 15.3 Thiết lập dịch vụ ngoài (ghi vào README)

- **Google Cloud Console:** tạo OAuth Client ID loại _Web application_; _Authorized JavaScript origins_: `https://{SITE_DOMAIN}`, `http://localhost:5173`. OAuth consent screen: scopes `openid email profile`, **chuyển trạng thái sang "In production"** (để "Testing" thì chỉ test users đăng nhập được).
- **Cloudinary:** Settings → Security → bật **"Allow delivery of PDF and ZIP files"** (tài khoản mới mặc định chặn, preview PDF sẽ lỗi 401); xem giới hạn dung lượng/file và credit của gói.

### 15.4 Makefile

```makefile
dev-deps:  ; docker compose -f docker-compose.dev.yml up -d          # db + gotenberg
api:       ; cd apps/api && dotnet watch --project src/HocLieu.Api
web:       ; cd apps/web && pnpm dev
gen-api:   ; cd apps/web && pnpm gen:api                              # từ http://localhost:8080/openapi/v1.json
migration: ; cd apps/api && dotnet ef migrations add $(name) -p src/HocLieu.Api
test:      ; cd apps/api && dotnet test && cd ../web && pnpm test
e2e:       ; cd apps/web && pnpm exec playwright test
up:        ; docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
```

### 15.5 CI (GitHub Actions)

- PR: `dotnet build` + `dotnet test` (Testcontainers dùng Docker của runner) · `pnpm lint && pnpm typecheck && pnpm test && pnpm build` · kiểm `api-types.ts` khớp OpenAPI hiện tại.
- `main`: build & push image `hoclieu-api`, `hoclieu-web` lên GHCR (tag = git sha + `latest`). Deploy thủ công: `ssh vm 'cd hoclieu/deploy && TAG=<sha> docker compose pull && docker compose up -d'`.

---

## 16. Milestones & tiêu chí nghiệm thu

| #                       | Phạm vi                                                                                                                                                                                  | Nghiệm thu                                                                                                                                                                                                                                                                         |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **M0** Nền tảng         | Monorepo, compose dev/prod, CI, health, OpenAPI, seed, layout FE + tokens, trang chủ rỗng                                                                                                | `make dev-deps api web` chạy; `make up` ra trang chủ qua HTTPS; CI xanh                                                                                                                                                                                                            |
| **M1** Đăng nhập        | Google login, cookie, `/me`, `/cho-duyet`, AdminEmails, `security_stamp`, chặn in-app browser                                                                                            | User mới → Pending; email admin → Admin; khóa tài khoản → phiên cũ mất hiệu lực ≤ 60s; restart container vẫn giữ phiên                                                                                                                                                             |
| **M2** Tổ & tài khoản   | CRUD tổ, bổ nhiệm tổ trưởng, tổ phó, hàng chờ duyệt, lời mời, thông báo in-app, audit log                                                                                                | Tổ trưởng tổ A gọi duyệt user xin vào tổ B → 403; mời email chưa có → đăng nhập bằng email đó → Active + vào tổ; email khác → bị từ chối; một tổ không thể có 2 tổ trưởng                                                                                                          |
| **M3** Tài liệu         | Upload + xử lý preview + thumbnail, CRUD tài liệu, phạm vi, ẩn/hiện/hẹn giờ, duyệt công khai theo khối/chuyên mục, tìm kiếm không dấu, chi tiết, tải về, yêu thích, báo lỗi              | docx/pptx/xlsx/pdf có preview từng trang; tài liệu phạm vi Tổ → khách và GV tổ khác nhận 404; tìm "ke hoach chu nhiem" ra "Kế hoạch chủ nhiệm"; khách không tải được khi tài liệu không cho phép                                                                                   |
| **M4** Bài tập          | Parser Word/Excel + test fixture §6.3, màn hình rà soát, cài đặt, hiển thị, làm bài, lưu tự động, chấm, kết quả, thống kê, xuất Excel                                                    | Quiz mới luôn Ẩn, khách vào URL → 404; hẹn T6 17:00 → CN 21:00: trước/sau khung không làm được, trong khung làm được, link kết quả cũ vẫn xem được; JSON làm bài không chứa `isCorrect`; file mẫu nhập đúng 100%; tải lại trang khi đang làm không mất câu trả lời; hết giờ tự nộp |
| **M5** Lớp học          | Lớp, GV bộ môn, nhập/xuất danh sách HS, giao bài (mã + QR), chế độ chọn tên, bảng điểm + xuất Excel                                                                                      | Nhập 40 HS từ file mẫu có 2 dòng lỗi → báo đúng dòng, không ghi khi dryRun; quiz đang Ẩn vẫn làm được qua mã giao bài trong khung giờ; HS chọn tên → điểm hiện đúng ô bảng điểm                                                                                                    |
| **M6** Admin hoàn thiện | Dashboard, quản lý nội dung, kiểm duyệt, báo cáo, danh mục, cài đặt, thông báo, trang tĩnh, chuyển quyền sở hữu, kết chuyển năm học, trang hệ thống                                      | Không khóa được admin cuối cùng; chuyển nội dung GV A → B đổi owner toàn bộ, ghi audit; kết chuyển năm: lớp 4A → 5A năm mới kèm HS, lớp 5 cũ lưu trữ                                                                                                                               |
| **M7** Hoàn thiện       | E2E Playwright (đăng nhập giả lập, tạo quiz từ Word, hẹn giờ, học sinh làm bài trên viewport mobile, xem kết quả), đo hiệu năng, header bảo mật, backup/khôi phục, tài liệu hướng dẫn GV | Đạt chỉ tiêu §13; khôi phục từ backup thành công trên máy sạch; `docs/huong-dan-giao-vien.md` hoàn chỉnh                                                                                                                                                                           |

Trong test tích hợp, thay `GoogleJsonWebSignature` bằng `IGoogleTokenValidator` giả (inject qua DI) để tạo user với email tùy ý.

---

## 17. Backlog phase 2 (chưa làm)

- Quy trình **duyệt kế hoạch bài dạy**: GV nộp KHBD theo tuần → Tổ trưởng góp ý/duyệt, lưu lịch sử phiên bản.
- Công thức toán đầy đủ: OMML → LaTeX, render KaTeX; hỗ trợ ảnh MathType (WMF/EMF → PNG).
- Loại câu mới: điền khuyết, nối cột, kéo thả, Đúng/Sai nhiều ý.
- Xuất đề ra Word/PDF (đề + đáp án), trộn nhiều mã đề.
- Ngân hàng câu hỏi theo bài/chủ đề, tạo đề theo ma trận.
- Đa trường (`schools`), phân quyền theo trường.
- Email tóm tắt hằng tuần, tích hợp Zalo OA.
- Prerender/SSR trang công khai cho SEO.
- Upload trực tiếp (signed) lên Cloudinary cho video lớn.
- Theo dõi tiến bộ học sinh theo thời gian.
- Trợ lý AI: sinh câu hỏi từ tài liệu, gợi ý lời nhận xét học sinh.

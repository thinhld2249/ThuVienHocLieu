# Hướng dẫn sử dụng cổng học liệu

Tài liệu hướng dẫn thao tác trên web cho **mọi vai trò**: khách (học sinh, phụ huynh), giáo viên chờ duyệt, giáo viên, tổ trưởng/tổ phó và Admin. Giao diện 100% tiếng Việt; các đường dẫn trong ngoặc kép là đường dẫn thực tế của web.

## Vai trò trong hệ thống

| Vai trò                     | Đăng nhập?                     | Khu vực chính                    | Ghi chú                                       |
| --------------------------- | ------------------------------ | -------------------------------- | --------------------------------------------- |
| Khách (học sinh, phụ huynh) | Không                          | Trang chủ, xem tài liệu, làm bài | Không cần tài khoản                           |
| GV chờ duyệt                | Google                         | `/cho-duyet` + khu công khai     | Phải được duyệt mới vào `/gv`                 |
| Giáo viên (Active)          | Google                         | `/gv`                            | Tạo nội dung, quản lý lớp của mình            |
| Tổ trưởng / Tổ phó          | Google                         | `/gv` + tab quản trị trong tổ    | Duyệt thành viên, mời GV, ẩn/hiện nội dung tổ |
| Admin                       | Google (hoặc email + mật khẩu) | `/admin` + mọi nơi               | Quản trị toàn hệ thống                        |

---

## 1. Khu công khai (không cần đăng nhập)

### 1.1 Trang chủ `/`

- **Ô tìm kiếm** trên cùng — gõ từ khóa (không cần dấu: `ke hoach` khớp `Kế hoạch`).
- **Chọn Khối** (chip 1–5) — lựa chọn được nhớ lại cho lần sau.
- **Bài tập đang mở** của khối đã chọn — thẻ hiển thị "Đang mở đến …".
- **Ô chuyên mục**: Phân phối chương trình · Kế hoạch bài dạy · Bài tập cuối tuần · Đề khảo sát · Chuyên đề · Kế hoạch chủ nhiệm. (Chuyên mục "Bài giảng điện tử" đang tạm ẩn — bật lại tại `/admin/danh-muc` nếu cần.)
- Thông báo ghim (nếu Admin đăng) và số liệu chung.

### 1.2 Duyệt nội dung

| Đường dẫn                       | Nội dung                                                                                                                 |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `/khoi/5`                       | Tất cả tài liệu + bài tập của khối 5; tab theo chuyên mục; lọc môn/tuần/năm học; sắp xếp Mới nhất/Xem nhiều; phân trang. |
| `/chuyen-muc/bai-tap-cuoi-tuan` | Nội dung theo chuyên mục; bài tập cuối tuần được nhóm theo Tuần (Tuần 31, 30, …).                                        |
| `/tim-kiem?q=toan+5`            | Tìm gộp tài liệu + bài tập, bộ lọc như trên.                                                                             |

### 1.3 Xem tài liệu `/tai-lieu/…`

- Tiêu đề + nhãn (chuyên mục, khối, môn, tuần, năm học), tác giả (nếu site cho phép hiện).
- **Xem trước từng trang** (ảnh lazy-load, xem to toàn màn hình).
- Nút **Tải về** — chỉ hiện khi tài liệu cho phép khách tải (quyết định của GV khi tạo).
- **Tài liệu liên quan** (cùng chuyên mục + khối).
- Nút **Báo lỗi nội dung** (phía dưới) — gửi lý do về cho Admin xem ở `/admin/bao-cao`.
- Giáo viên đã đăng nhập có thêm nút **Lưu** (thêm vào Yêu thích).

Nội dung bị ẩn / hết hạn / phạm vi không cho khách → web trả về trang "Không tìm thấy".

### 1.4 Làm bài tập (trắc nghiệm)

**Từ trang giới thiệu** `/bai-tap/…`:

1. Xem tên, khối/môn/tuần, số câu, thời gian, trạng thái (**Đang mở đến …** / **Sắp mở** / **Đã đóng**).
2. Nếu bài yêu cầu nhận diện: nhập họ tên (và/lớp tùy cài đặt của GV).
3. Bấm **Bắt đầu làm bài**. Nếu máy này đã có lượt làm dở → hiện nút **Làm tiếp**.

**Trong khi làm** `/lam-bai/…` (thiết kế cho điện thoại):

- 1 câu 1 màn hình, phương án là **thẻ lớn** — chạm toàn thẻ để chọn.
- Thanh tiến trình + **lưới câu** (đã làm / chưa làm / đánh dấu xem lại).
- **Đồng hồ đếm ngược** (nếu bài có giới hạn giờ) — hết giờ hệ thống tự nộp.
- Nút **A−/A+** chỉnh cỡ chữ; đoạn văn của nhóm thu gọn phía trên câu hỏi.
- **Tự động lưu** mỗi khi trả lời (debounce ~1,5s). Tắt máy/tải lại trang vẫn làm tiếp được (câu trả lời lưu trong trình duyệt).
- Nút **Nộp bài** — xác nhận số câu chưa làm trước khi nộp.

**Trang kết quả** `/ket-qua/…`:

- Điểm thang 10, số câu đúng, thời gian làm.
- **Xem lại từng câu** (đã chọn / đáp án đúng / giải thích) — có hoặc không tùy GV cài đặt "Hiện đáp án" (không bao giờ / sau khi nộp / sau khi đóng bài).
- Nút **Làm lại** nếu còn lượt.

### 1.5 Vào lớp bằng mã giao bài `/vao-lop`

GV giao bài sẽ gửi phụ huynh **mã 6 ký tự** (hoặc mã QR).

1. Mở link `/vao-lop?ma=ABC123` (từ QR), hoặc vào `/vao-lop` và tự nhập mã.
2. Nếu bài yêu cầu **chọn tên trong danh sách lớp**: chọn đúng tên của con → làm bài.
3. Nếu không: nhập tên tự do.

### 1.6 Trang tĩnh `/trang/…`

Giới thiệu, hướng dẫn, chính sách dữ liệu — do Admin soạn.

### 1.7 Lưu ý trình duyệt trong app (Zalo/Facebook)

- **Google chặn đăng nhập trong webview** → học sinh làm bài không bị ảnh hưởng (không cần đăng nhập).
- Giáo viên vào trong Zalo sẽ thấy hướng dẫn "Mở bằng Chrome/Safari" ở trang `/dang-nhap`. Admin có thể dùng **email + mật khẩu** làm đường dự phòng (mục 2.2).

---

## 2. Đăng nhập & tài khoản

### 2.1 Trang `/dang-nhap`

- **Nút Google** (phương thức chính): bấm → chọn tài khoản Google (Gmail cá nhân hoặc Google Workspace của trường) → được đưa vào web.
- **Khung email + mật khẩu** (dưới ngăn "hoặc"): chỉ dùng cho tài khoản **Admin** đã đặt mật khẩu. Tài khoản khác nhập vào sẽ nhận thông báo "chưa đặt mật khẩu".
- Nếu đang ở trong app (Zalo) → web hiện hướng dẫn mở bằng trình duyệt riêng.
- Sau đăng nhập, web tự đưa về trang bạn định đến trước đó.

### 2.2 Lần đầu đăng nhập (tài khoản mới)

| Trường hợp                                                 | Kết quả                                  |
| ---------------------------------------------------------- | ---------------------------------------- |
| Email nằm trong danh sách Admin (do vận hành cấu hình sẵn) | Vào thẳng, vai trò **Admin**             |
| Đang giữ link lời mời `/moi/…` và email khớp               | **Active** ngay + tự vào tổ theo lời mời |
| Domain email thuộc danh sách tự duyệt (Admin cấu hình)     | **Active**, chưa có tổ                   |
| Mọi trường hợp còn lại                                     | **Chờ duyệt** → chuyển sang `/cho-duyet` |

**Trang `/cho-duyet`** (GV mới): nhập lại họ tên đầy đủ, SĐT (tùy chọn), **chọn tổ muốn tham gia** → gửi yêu cầu. Chờ Tổ trưởng/Tổ phó tổ đó (hoặc Admin) duyệt. Khi được duyệt → vào được `/gv` và đã là thành viên tổ.

### 2.3 Nhận lời mời `/moi/…`

- Mở link lời mời → bấm đăng nhập Google → hệ thống kiểm tra email khớp lời mời.
- Khớp → tài khoản **Active** + vào tổ (vai trò theo lời mời, thường là Thành viên; Admin có thể mời làm Tổ trưởng).
- Không khớp → thông báo lời mời này dành cho email khác (mã hóa phần đuôi).
- Link hết hạn 7 ngày — có thể yêu cầu người mời **gửi lại** (token mới).

### 2.4 Quản lý phiên

- **Đăng xuất** — nút hình mũi tên ra ở góc phải trên cùng (bên cạnh chuông thông báo).
- **Đăng xuất mọi thiết bị** — ở `/gv/ho-so`, mục "Phiên đăng nhập". Mọi phiên cũ (kể cả điện thoại của con/bà con) hết hiệu lực ngay.
- Khi bị Admin khóa tài khoản: mọi phiên hiện có mất hiệu lực trong ≤ 60 giây.

### 2.5 Hồ sơ `/gv/ho-so`

- Cập nhật **Họ và tên**, **Số điện thoại**.
- Xem **Tổ chuyên môn** mình đang tham gia + vai trò (Tổ trưởng/Tổ phó/Thành viên).
- **Mật khẩu đăng nhập** — chỉ hiện với tài khoản Admin: đặt mật khẩu 8–128 ký tự để đăng nhập bằng email + mật khẩu.
- **Đăng xuất mọi thiết bị**.

---

## 3. Khu giáo viên `/gv`

Chỉ giáo viên **Active** vào được. Sidebar trái: Tổng quan · Tài liệu · Bài tập · Lớp · Tổ · Yêu thích (+ "Quản trị" nếu bạn là Admin). Góc phải trên cùng: chip tài khoản (vào Hồ sơ), chuông thông báo, nút đăng xuất.

### 3.1 Tổng quan `/gv`

Số tài liệu/bài tập của bạn, lượt làm bài 7 ngày, bài tập đang mở, thông báo, nút tạo nhanh.

### 3.2 Tài liệu `/gv/tai-lieu`

**Bảng của tôi** — mỗi hàng có cột **trạng thái hiển thị** (Đang hiện / Đang ẩn / Sắp mở / Đang mở / Đã đóng) và cột thao tác: Sửa · Nhân bản · Xóa (xóa mềm — khôi phục được trong 30 ngày).

- **Hiện/Ẩn**: switch trong bảng — đổi ngay.
- **Hẹn giờ**: nút bấm → chọn Từ/Đến (giờ VN), có preset sẵn (ví dụ "Cuối tuần này T6 17:00 → CN 21:00"). Hết khung giờ tự động ẩn, không cần job chạy.
- Chọn nhiều hàng → thao tác hàng loạt (Hiện, Ẩn, Hẹn giờ).

**Tạo/sửa tài liệu** `/gv/tai-lieu/moi` hoặc `/gv/tai-lieu/:id`:

| Nhóm               | Trường                                                                                                                                                                                                    |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Nội dung           | Tiêu đề, mô tả (mỗi dòng là một đoạn), tags, ảnh bìa (tùy chọn). **Video**: dán link YouTube / Vimeo / Google Drive vào một dòng riêng trong mô tả → hệ thống tự nhúng video ngay vào trang tài liệu.     |
| Phân loại          | **Chuyên mục** (bắt buộc), **Khối** (bắt buộc, trừ Hồ sơ tổ), Môn học (tùy chọn), Năm học (mặc định năm hiện tại), **Tuần 1–35** (bắt buộc với Bài tập cuối tuần), gắn Lớp (tùy chọn, ví dụ KH chủ nhiệm) |
| File               | **Kéo-thả nhiều file** (tối đa 10; có thanh tiến trình + trạng thái xử lý preview). Cho phép .pdf/.docx/.pptx/.xlsx/.jpg… theo cài đặt của site — **không nhận file video** (dùng link nhúng ở ô mô tả)   |
| Phạm vi & hiển thị | Phạm vi xem (**Công khai** / Giáo viên / **Tổ** / Riêng mình), Hiện/Ẩn/Hẹn giờ, ô "Cho phép khách tải về"                                                                                                 |

Sau khi upload, hệ thống tự sinh **preview từng trang** (qua Gotenberg cho Office) — xem được ngay trong trang chi tiết công khai.

### 3.3 Bài tập `/gv/bai-tap`

**Tạo bài tập mới** — 3 cách:

1. **Tạo từ Word (.docx)** — tải file lên → hệ thống tự nhận diện câu hỏi, phương án, đáp án → tạo quiz ở trạng thái **Ẩn** → vào màn hình **rà soát**. Định dạng file chi tiết: `docs/quiz-import-format.md`; file mẫu: [mau-bai-tap.docx](/templates/mau-bai-tap.docx).
2. **Tạo từ Excel (.xlsx)** — sheet đầu, hàng 1 là tiêu đề (STT · Câu hỏi · A–F · Đáp án · Giải thích · Nhóm · Đoạn văn · Điểm); đọc bằng chuỗi đã định dạng nên "5,7" không biến thành số. File mẫu: [mau-bai-tap.xlsx](/templates/mau-bai-tap.xlsx).
3. **Tạo trống** — tự nhập từng câu.

**Màn hình rà soát sau import**: banner "Đã nhận diện N câu — M câu cần kiểm tra" + nút lọc câu có cảnh báo (thiếu đáp án, trùng phương án, nhảy số câu, …). Sửa trực tiếp từng câu trước khi công bố.

**Trang chi tiết bài tập** `/gv/bai-tap/:id` — 6 tab:

| Tab              | Nội dung                                                                                                                                                                                                                                                                                                                                                                                                                  |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Câu hỏi**      | Danh sách câu bên trái (số, trích đề, icon ✓/⚠, kéo-thả sắp xếp) + editor bên phải: đề (rich text + ảnh), loại câu (1 đáp án / chọn nhiều / Đúng–Sai), phương án (thêm/xóa/sắp xếp), giải thích, điểm. Thao tác hàng loạt: đặt điểm cho tất cả, xóa nhiều câu. Nút "Xem như học sinh".                                                                                                                                    |
| **Cài đặt**      | **Hiển thị bài tập cho**: mọi người (hiện ở trang chủ khi Hiện/Hẹn giờ) hoặc **chỉ học sinh lớp đã giao bài** (qua mã/QR — không lộ ở khu công khai). Thời gian làm (phút, trống = không giới hạn), trộn câu, trộn phương án, hiện đáp án (không bao giờ / sau khi nộp / sau khi đóng), nhận diện (vô danh / tên / tên+lớp), số lượt tối đa, chấm câu chọn nhiều (toàn bộ hoặc từng phần), làm tròn điểm, file đề in kèm. |
| **Hiển thị**     | Hiện/Ẩn/Hẹn giờ + **Phạm vi xem** + mô tả trạng thái bằng lời ("Đang mở đến 21:00 CN 11/10").                                                                                                                                                                                                                                                                                                                             |
| **Giao cho lớp** | Tạo assignment (mục 3.4).                                                                                                                                                                                                                                                                                                                                                                                                 |
| **Kết quả**      | Bảng lượt làm: họ tên · lớp · điểm · số câu đúng · thời gian · lượt thứ; lọc theo lớp; xóa lượt rác. **Xuất Excel** (sheet Kết quả + Thống kê câu).                                                                                                                                                                                                                                                                       |
| **Thống kê**     | Phân bố điểm, điểm TB/trung vị, **% đúng từng câu** — câu dưới 30% được tô nổi để bạn kiểm tra lại đáp án.                                                                                                                                                                                                                                                                                                                |

**Lưu ý sửa quiz đã có lượt làm**: sửa chữ tự do thoải mái; nhưng **đổi đáp án đúng / xóa câu / xóa phương án** → hệ thống hỏi xác nhận và **chấm lại toàn bộ lượt làm** đã nộp. Nhập lại từ file mới chỉ được khi chưa có lượt làm nào.

### 3.4 Lớp học `/gv/lop`

**Tạo lớp mới**: tên (vd 5A), khối, năm học, tổ. Tên lớp duy nhất trong một năm học. Bạn tự động là GV chủ nhiệm.

**Trang lớp** `/gv/lop/:id` — 3 tab:

| Tab             | Nội dung                                                                                                                                                                                                                                                                                                                              |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Học sinh**    | Nhập từ Excel [mau-danh-sach-hs.xlsx](/templates/mau-danh-sach-hs.xlsx) — hệ thống **xem trước & báo lỗi từng dòng** trước khi ghi; trùng (tên + ngày sinh) bị bỏ qua và báo. Thêm/sửa/sắp xếp/ngưng hoạt động thủ công; xuất Excel. Xóa học sinh → xóa luôn lượt làm liên quan (có xác nhận). File Excel gốc **không** được lưu lại. |
| **Bài đã giao** | Chọn quiz (của bạn **hoặc quiz công khai của đồng nghiệp**) → tạo assignment: **mã 6 ký tự**, giờ mở/đóng, chế độ **chọn tên trong danh sách lớp** hoặc nhập tên tự do. Hiện **link + mã QR + nút sao chép** để gửi nhóm Zalo phụ huynh. Quiz đang **Ẩn** vẫn giao được bình thường qua mã.                                           |
| **Bảng điểm**   | Hàng = học sinh, cột = bài đã giao, ô = điểm cao nhất (ô trống tô nổi = chưa làm). Xuất Excel.                                                                                                                                                                                                                                        |

### 3.5 Yêu thích `/gv/yeu-thich`

Tài liệu + bài tập bạn đã bấm **Lưu** ở trang công khai. GV có thể **nhân bản** bài tập của đồng nghiệp về làm bản của mình rồi giao cho lớp.

### 3.6 Không gian tổ `/gv/to` (→ `/gv/to/:teamId`)

Nếu bạn thuộc nhiều tổ → trang `/gv/to` cho chọn. Tab trong trang tổ:

| Tab              | Thành viên                                                   | Tổ trưởng / Tổ phó                                                                                                                                                       |
| ---------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Thành viên**   | Xem danh sách + vai trò                                      | Thêm: đặt/bỏ **Tổ phó**, gỡ thành viên (chỉ Tổ trưởng)                                                                                                                   |
| **Chờ duyệt**    | —                                                            | Xem GV xin vào tổ (tên, email, ảnh, thời điểm) → **Duyệt** / **Từ chối** (kèm lý do); duyệt hàng loạt                                                                    |
| **Lời mời**      | —                                                            | Nhập nhiều email (xuống dòng/dấu phẩy, tối đa 100), lời nhắn tùy chọn → link mời + nút **Sao chép tất cả** (dán vào Zalo). Theo dõi trạng thái, **gửi lại**, **thu hồi** |
| **Thông báo tổ** | Đăng thông báo chỉ thành viên tổ thấy                        | ✓                                                                                                                                                                        |
| **Thống kê**     | Số nội dung theo thành viên × chuyên mục, lượt làm theo tuần | ✓                                                                                                                                                                        |

Nội dung có phạm vi **Tổ** (Hồ sơ tổ) của các thành viên xem được tại đây; Tổ trưởng/Tổ phó được **ẩn/hiện** nội dung của thành viên (không được xóa).

### 3.7 Giáo viên & bài tập của đồng nghiệp

- Dùng bài tập **công khai** của GV khác để giao thẳng cho lớp mình (tab Giao cho lớp).
- Hoặc **Nhân bản** về làm bản của mình (sửa thoải mái, không đụng bản gốc).

---

## 4. Tổ trưởng / Tổ phó (quyền thêm)

- **Duyệt / từ chối** GV xin vào tổ (tab Chờ duyệt) — duyệt hàng loạt được.
- **Mời GV** vào tổ vai trò Thành viên (tab Lời mời).
- **Đặt / bỏ Tổ phó**, **gỡ thành viên** — chỉ Tổ trưởng.
- **Ẩn/hiện nội dung** của thành viên trong tổ (kể cả đang ẩn) — không xóa.
- **Duyệt nội dung** của thành viên khi site bật kiểm duyệt (Admin bật ở Cài đặt).
- Nội dung do thành viên tạo vẫn thuộc "sở hữu" của GV đó; khi GV rời tổ, nội dung vẫn do tổ quản lý.

Admin bổ nhiệm/miễn nhiệm Tổ trưởng ở `/admin/to`. Một tổ chỉ có **một** Tổ trưởng — bổ nhiệm người mới, người cũ tự thành Thành viên.

---

## 5. Khu Admin `/admin`

Chỉ tài khoản Admin. Sidebar 13 mục:

| Trang             | Đường dẫn           | Làm được gì                                                                                                                                                                                                                                                                                              |
| ----------------- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Tổng quan**     | `/admin`            | Số user theo trạng thái, **hàng chờ duyệt** (duyệt/từ chối nhanh), **tổ chưa có tổ trưởng** (cảnh báo), nội dung mới 7/30 ngày, lượt làm bài, báo cáo vi phạm đang mở, dung lượng lưu trữ, file xử lý lỗi.                                                                                               |
| **Người dùng**    | `/admin/nguoi-dung` | Bảng user (lọc trạng thái/vai trò/tổ, tìm): **duyệt/từ chối** (kèm lý do), **khóa/mở khóa**, **cấp/thu quyền Admin**, gán tổ, xem nội dung của user, **chuyển quyền sở hữu nội dung** sang GV khác (khi GV nghỉ/chuyển trường), **đăng xuất mọi thiết bị**. Không khóa được Admin cuối cùng đang Active. |
| **Tổ chuyên môn** | `/admin/to`         | Tạo/sửa tổ (tên, mô tả, khối phụ trách), **bổ nhiệm Tổ trưởng**, đặt Tổ phó, thêm/bớt thành viên, **ngừng hoạt động** tổ.                                                                                                                                                                                |
| **Lời mời**       | `/admin/loi-moi`    | Mời vào **bất kỳ tổ, vai trò nào** (kể cả Tổ trưởng); xem mọi lời mời (500 mới nhất), gửi lại, thu hồi.                                                                                                                                                                                                  |
| **Nội dung**      | `/admin/noi-dung`   | Mọi tài liệu & bài tập (kể cả riêng tư, có gắn nhãn): lọc theo GV/tổ/chuyên mục/trạng thái; **ghim nổi bật trang chủ**, ẩn/hiện, **đổi chuyên mục**, xóa mềm/khôi phục, duyệt nội dung (khi bật kiểm duyệt).                                                                                             |
| **Báo cáo**       | `/admin/bao-cao`    | Báo lỗi từ người xem: xem nội dung bị báo, xử lý/bỏ qua, ghi chú.                                                                                                                                                                                                                                        |
| **Danh mục**      | `/admin/danh-muc`   | **Chuyên mục** (tên, icon, màu, thứ tự, hiển thị mặc định, bắt buộc tuần, bật/tắt), **Khối**, **Môn học**, **Năm học** (đặt năm hiện tại), **Tags**.                                                                                                                                                     |
| **Lớp**           | `/admin/lop`        | Mọi lớp; **đổi GV chủ nhiệm**.                                                                                                                                                                                                                                                                           |
| **Năm học**       | `/admin/nam-hoc`    | Tạo năm mới, đặt năm hiện tại, **kết chuyển năm học**: lưu trữ lớp năm cũ, tùy chọn **nhân bản lớp lên khối +1 kèm học sinh** (lớp khối 5 chỉ lưu trữ).                                                                                                                                                  |
| **Thông báo**     | `/admin/thong-bao`  | Đăng thông báo (Công khai / Giáo viên / Tổ), ghim lên trang chủ; soạn **trang tĩnh** (Giới thiệu, Hướng dẫn, Chính sách dữ liệu…) bằng Markdown.                                                                                                                                                         |
| **Cài đặt**       | `/admin/cai-dat`    | **Khối trang**: tên site, logo, liên hệ · **Đăng nhập & nội dung**: domain tự duyệt, bật kiểm duyệt nội dung, hiện tên tác giả công khai, khách được tải mặc định · **Tải file lên**: dung lượng tối đa, định dạng cho phép.                                                                             |
| **Nhật ký**       | `/admin/nhat-ky`    | Audit log mọi thao tác quan trọng (đăng nhập, duyệt, ẩn/hiện, xóa, xuất kết quả, đổi cài đặt…): lọc theo người/hành động/thời gian, **xuất CSV**.                                                                                                                                                        |
| **Hệ thống**      | `/admin/he-thong`   | Trạng thái DB/Cloudinary/Gotenberg, **hàng đợi xử lý file** (chạy lại job lỗi), phiên bản app.                                                                                                                                                                                                           |

---

## 6. Bảng tóm tắt quyền nhanh

| Chức năng                                               | Khách | GV mới (chờ duyệt) | Giáo viên | Tổ trưởng/Tổ phó  |   Admin    |
| ------------------------------------------------------- | :---: | :----------------: | :-------: | :---------------: | :--------: |
| Xem/tải nội dung công khai, làm bài                     |   ✓   |         ✓          |     ✓     |         ✓         |     ✓      |
| Xem nội dung phạm vi "Tổ" của tổ mình                   |       |                    |     ✓     |         ✓         | ✓ (mọi tổ) |
| Tạo/sửa/xóa nội dung của mình                           |       |                    |     ✓     |         ✓         |     ✓      |
| Ẩn/hiện nội dung của thành viên trong tổ                |       |                    |           |         ✓         |     ✓      |
| Duyệt GV xin vào tổ, mời GV                             |       |                    |           |         ✓         | ✓ (mọi tổ) |
| Đặt/bỏ tổ phó, gỡ thành viên                            |       |                    |           | ✓ (chỉ tổ trưởng) |     ✓      |
| Bổ nhiệm tổ trưởng, tạo tổ                              |       |                    |           |                   |     ✓      |
| Khóa user, cấp quyền, chuyển sở hữu nội dung            |       |                    |           |                   |     ✓      |
| Cài đặt, danh mục, báo cáo, nhật ký, kết chuyển năm học |       |                    |           |                   |     ✓      |

---

## 7. Câu hỏi thường gặp

**GV: Vào `/gv` mà thấy trang "chờ duyệt"?**
Tài khoản của bạn chưa được duyệt. Kiểm tra lại yêu cầu ở `/cho-duyet` đã gửi chưa (họ tên, tổ); nếu đã gửi thì chờ tổ trưởng/tổ phó hoặc Admin duyệt.

**GV: Trang "Tổ" trống, không thấy tổ mình?**
Tài khoản chưa được duyệt vào tổ nào, hoặc tổ đã bị **ngừng hoạt động** (biến mất khỏi danh sách chọn). Liên hệ Admin.

**Học sinh: Mở link mà báo "Không tìm thấy trang"?**
Bài tập đang ở trạng thái **Sắp mở** hoặc **Đã đóng** (hết giờ hiển thị) — hoặc link của bài đang ẩn mà em không có mã giao bài. Hãy hỏi giáo viên gửi lại link/mã QR khi bài đã mở.

**Học sinh: Làm dở bị tắt máy/mất mạng?**
Mở lại link làm bài — câu trả lời được tự lưu trong trình duyệt; hoặc vào lại qua mã giao bài để làm tiếp (trong hạn lượt).

**Giáo viên: Upload file xong không thấy preview?**
Xử lý preview chạy nền (vài chục giây với file lớn). Xem trạng thái trong form (đang xử lý / lỗi). File lỗi có thể xem lại ở `/admin/he-thong` (Admin) để chạy lại.

**Admin: Muốn GV nghỉ tiếp quản nội dung?**
`/admin/nguoi-dung` → chọn user → **Chuyển quyền sở hữu** sang GV khác (toàn bộ tài liệu + bài tập, có ghi nhật ký).

**Tại sao đăng nhập bằng webview Zalo bị chặn?**
Google không cho đăng nhập trong webview của app bên thứ ba. Mở lại link bằng Chrome/Safari (web có nút hướng dẫn), hoặc Admin dùng email + mật khẩu.

**Làm sao xem lại đáp án sau khi nộp bài?**
Tùy giáo viên cài đặt: không bao giờ / sau khi nộp / sau khi bài đóng. Trang kết quả tự hiện phần "Xem lại từng câu" khi được phép.

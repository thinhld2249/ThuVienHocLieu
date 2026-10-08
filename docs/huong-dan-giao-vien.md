# Hướng dẫn cho giáo viên

Học Liệu là nơi giáo viên lưu tài liệu và tạo bài tập trắc nghiệm cho học sinh. Học sinh và phụ huynh **không cần tài khoản** — em nào cũng làm được bài ngay trên điện thoại, kể cả mở từ link Zalo.

Mọi giao diện dùng tiếng Việt. Giờ hiển thị theo giờ Việt Nam; ngày tháng dạng `dd/MM/yyyy`.

---

## 1. Đăng nhập

1. Mở trang chủ → nút **Đăng nhập** (phía trên bên phải) → bấm **Đăng nhập với Google**.
2. Dùng tài khoản Gmail cá nhân hoặc Google của trường.

Lần đầu đăng nhập, tài khoản ở trạng thái **chờ duyệt**:

- Nếu trường của bạn **tự động duyệt** domain email (do quản trị viên bật), bạn vào dùng ngay.
- Nếu chưa được duyệt: trang **Chờ duyệt** hiện ra — nhập họ tên đầy đủ, số điện thoại (tùy chọn), chọn **tổ chuyên môn** bạn muốn tham gia rồi gửi. Tổ trưởng/tổ phó (hoặc quản trị viên) duyệt, bạn nhận thông báo ngay trong hệ thống.

> **Mở trong ứng dụng Zalo/Facebook?** Google chặn đăng nhập trong trình duyệt tích hợp của các app đó. Trang đăng nhập sẽ hiện hướng dẫn: bấm **Mở bằng Chrome/Safari** (hoặc sao chép link rồi mở ở trình duyệt thường). Học sinh làm bài không bị ảnh hưởng — em ấy không cần đăng nhập.

Sau khi vào, khu giáo viên ở `/gv`, menu trái gồm: **Tổng quan · Tài liệu · Bài tập · Lớp · Tổ · Yêu thích**.

---

## 2. Tạo tài liệu (bài giảng, KHBD, PPCT, chuyên đề…)

### 2.1. Nạp tài liệu

1. Vào **Tài liệu** → **Tạo tài liệu**.
2. Điền: **Tiêu đề** (bắt buộc), **Chuyên mục**, **Khối**, **Môn học** (tùy chọn), **Năm học**, **Tuần** (bắt buộc với Bài tập cuối tuần), **Phạm vi xem**, tags, mô tả, ảnh bìa (tùy chọn).
3. Kéo-thả (hoặc bấm chọn) file vào khu vực nạp — nạp được **nhiều file cùng lúc** (tối đa 10), có thanh tiến trình. Hệ thống tự tạo **preview từng trang** cho docx/pptx/xlsx/pdf (qua Gotenberg).

Định dạng nhận: `pdf, doc, docx, ppt, pptx, xls, xlsx, jpg, jpeg, png, webp, mp4` (giới hạn 50 MB/file — quản trị viên có thể đổi). File `.doc/.xls/.ppt` cũ vẫn nạp được, preview được chuyển qua PDF.

### 2.2. Ai xem được?

| Phạm vi       | Ai xem được                   |
| ------------- | ----------------------------- |
| **Công khai** | Mọi người, kể cả khách        |
| **Giáo viên** | GV đã đăng nhập và được duyệt |
| **Tổ**        | Thành viên tổ của tài liệu    |
| **Riêng tư**  | Chỉ bạn (+ quản trị viên)     |

Quản trị viên có thể bật **kiểm duyệt**: khi đó tài liệu Công khai mới của bạn chờ tổ trưởng/tổ phó hoặc quản trị viên **Duyệt** trước khi lên web.

### 2.3. Hiện / ẩn / hẹn giờ

Mọi tài liệu và bài tập đều có nút **Hiện / Ẩn / Hẹn giờ** (cột cố định trong bảng Tài liệu, tab **Hiển thị** của bài tập):

- **Hiện / Ẩn** — chuyển trạng thái, có hiệu lực ngay (nút Hiện → thông báo "Đã hiện…").
- **Hẹn giờ** — chọn giờ **Từ** / **Đến** (theo giờ VN). Có sẵn:
  - _Cuối tuần này_: Thứ Sáu 17:00 → Chủ nhật 21:00 (phù hợp bài tập cuối tuần — cao điểm tối T6–CN).
  - _Từ bây giờ, 7 ngày_.
  - _Tùy chỉnh_.

Trạng thái hiển thị: **Đang ẩn · Đang hiện · Sắp mở · Đang mở · Đã đóng** — hệ thống tính tự động theo giờ, không cần bật lại.

> Link hẹn giờ: trang giới thiệu bài tập (đường dẫn `/bai-tap/…`) **vẫn mở được với học sinh cả khi đang Sắp mở hoặc Đã đóng** — em ấy thấy trạng thái "Sắp mở lúc…"/"Đã đóng". Chỉ nút **Bắt đầu làm bài** là chặn khi ngoài khung giờ.

Chọn nhiều tài liệu → thao tác hàng loạt (Hiện, Ẩn, Hẹn giờ).

### 2.4. Chỉnh sửa, nhân bản, xóa

- **Sửa** — mở form, sửa, bấm **Lưu**. Nếu người khác đã sửa trước → hệ thống báo "đã được thay đổi bởi người khác" và làm mới.
- **Nhân bản** — tạo bản sao để sửa riêng.
- **Xóa** — xóa mềm; tài liệu nằm ở **thùng rác** 30 ngày, bấm **Khôi phục** nếu nhầm.

### 2.5. Cho khách tải file?

Mặc định khách **không** tải được file (chỉ xem preview). Khi tạo tài liệu, tick **Cho phép khách tải** nếu muốn. Khách tải qua link có hạn 10 phút; số lượt tải hiện ở trang chi tiết.

---

## 3. Tạo bài tập trắc nghiệm

### 3.1. Ba cách tạo

Vào **Bài tập**:

| Nút              | Khi nào dùng                                               |
| ---------------- | ---------------------------------------------------------- |
| **Tạo từ Word**  | Bạn đã soạn đề .docx — nhanh nhất, giữ được định dạng, ảnh |
| **Tạo từ Excel** | Đề dạng bảng (cột Câu hỏi / A…F / Đáp án)                  |
| **Tạo trống**    | Soạn trực tiếp trong hệ thống                              |

File mẫu: [mau-bai-tap.docx](/templates/mau-bai-tap.docx) · [mau-bai-tap.xlsx](/templates/mau-bai-tap.xlsx).

Định dạng Word chi tiết (đánh dấu đáp án 4 cách, phương án nhiều trên 1 dòng, bảng, nhóm + đoạn văn, câu Đúng/Sai, chọn nhiều, ảnh trong đề): xem **[Định dạng file bài tập](quiz-import-format.md)**.

### 3.2. Màn rà soát (bắt buộc trước khi phát hành)

Sau khi nhập, bài tập được tạo ở trạng thái **Ẩn** và mở màn **rà soát**:

- Banner trên đầu: "Đã nhận diện N câu — M câu cần kiểm tra" + nút lọc câu có cảnh báo.
- Cảnh báo phổ biến: **chưa xác định được đáp án đúng** (bỏ đánh dấu), **thiếu phương án**, **nhảy số câu**, **xung đột nguồn đáp án** (dùng 2 cách đánh dấu cho ra 2 kết quả khác nhau), **ảnh không hiển thị được** (MathType/WMF — thay bằng ảnh PNG).
- Editor từng câu: đề (đậm/nghiêng/gạch chân/chỉ số trên-dưới), loại câu, phương án (radio cho 1 đáp án, checkbox cho chọn nhiều), giải thích, điểm, nhóm (tiêu đề + đoạn văn).
- **Kéo-thả** để sắp xếp câu; thao tác hàng loạt (đặt điểm tất cả, xóa nhiều câu); nút **Xem như học sinh** để xem đúng giao diện em ấy thấy.

Lưu ý quan trọng:

- Sửa **chữ** trong đề/phương án: sửa thoải mái, kể cả khi học sinh đã làm.
- Đổi **đáp án đúng / xóa câu / xóa phương án** khi đã có lượt làm → hệ thống báo số lượt sẽ bị chấm lại, bạn xác nhận → chấm lại toàn bộ.
- **Nhập lại file mới** (thay toàn bộ câu hỏi) chỉ được khi **chưa có lượt làm**.

### 3.3. Cài đặt bài tập

Tab **Cài đặt**:

- **Thời gian làm** (phút; trống = không giới hạn).
- **Trộn câu** / **trộn phương án** — mỗi học sinh nhận thứ tự riêng; nhãn A/B/C/D gắn theo thứ tự hiển thị.
- **Hiện đáp án**: _Không bao giờ_ / _Sau khi nộp_ / _Sau khi đóng_.
- **Nhận diện**: _Ký ẩn danh_ / _Nhập tên_ / _Tên + lớp_.
- **Số lượt tối đa** (trống = không giới hạn) — giới hạn mềm theo thiết bị (hoặc theo học sinh khi giao qua danh sách lớp).
- **Chấm câu chọn nhiều**: _Đúng hết mới tính điểm_ (mặc định) / _Tính từng phần_.
- **Làm tròn điểm**: không / 0,25 / 0,5 / số nguyên.
- **File đề in kèm** (tùy chọn) — học sinh thấy nút "Đề in" ở trang giới thiệu.

### 3.4. Phát hành

Tab **Hiển thị** (hoặc cột PublishControl ở danh sách **Bài tập**):

1. **Hiện ngay** — học sinh vào link `/bai-tap/…` là làm được.
2. **Hẹn giờ** — ví dụ T6 17:00 → CN 21:00 (preset "Cuối tuần này").
3. **Giao cho lớp** (mục 4 bên dưới) — link + mã + QR gửi nhóm Zalo phụ huynh; chỉ học sinh của lớp đó làm trong khung giờ giao.

---

## 4. Lớp học & giao bài

### 4.1. Tạo lớp

1. Vào **Lớp** → **Tạo lớp**: tên (vd `5A`), khối, năm học, bạn tự động là **GV chủ nhiệm**.
2. Thêm **GV bộ môn** (kèm môn) nếu lớp có GV khác cùng quản lý.
3. Tên lớp chỉ duy nhất trong một năm học.

### 4.2. Danh sách học sinh

Tab **Học sinh** của trang lớp:

- **Nhập từ Excel** — file mẫu [mau-danh-sach-hs.xlsx](/templates/mau-danh-sach-hs.xlsx) (STT · Họ và tên\* · Ngày sinh · Giới tính · Mã HS). Hệ thống **xem trước** và báo lỗi/trùng **từng dòng** trước khi bạn xác nhận ghi (chỉ cột Họ và tên bắt buộc; ngày sinh nhận nhiều dạng `dd/MM/yyyy`, `yyyy-MM-dd`…).
- Thêm/sửa/sắp xếp/tạm dừng từng học sinh thủ công; **xuất Excel**.
- Hệ thống **không lưu file Excel gốc** của danh sách học sinh — giảm rủi ro dữ liệu cá nhân.

### 4.3. Giao bài (mã + QR)

Tab **Bài đã giao**:

1. Chọn bài tập (của bạn **hoặc** bài tập Công khai của đồng nghiệp).
2. Đặt **khung giờ mở/đóng**, chọn **chế độ danh sách lớp** (học sinh chọn tên mình trong danh sách — điểm gắn vào đúng em) hoặc **nhập tên tự do**.
3. Hệ thống tạo **mã 6 ký tự** (không có ký tự dễ nhầm 0/O/1/I/L) + **mã QR** + nút **Sao chép link** — gửi thẳng nhóm Zalo phụ huynh.

- Học sinh mở link → thấy tên lớp → chọn tên mình → làm bài. Ngoài khung giờ, trang chỉ hiện trạng thái.
- Bài tập **đang Ẩn** vẫn giao được qua mã — trạng thái ẩn/hiện công khai và việc giao cho lớp là 2 thứ độc lập.
- Hết giờ: học sinh làm dở bị **chốt tự động** (chấm điểm phần đã làm, đánh dấu Hết hạn).

### 4.4. Bảng điểm

Tab **Bảng điểm**: hàng = học sinh, cột = bài đã giao, ô = **điểm cao nhất** (thang 10, chữ số thập phân dùng dấu phẩy). Ô trống tô nổi = chưa làm. Nút **Xuất Excel**.

---

## 5. Kết quả & thống kê bài tập

Trong **Bài tập** → mở bài → tab **Kết quả**:

- **Bảng lượt làm**: Họ tên · Lớp · Điểm · Số câu đúng · Thời gian · Nộp lúc · Lượt thứ. Lọc theo lớp/bài giao; chọn xem Lượt cao nhất/đầu/cuối; xóa lượt rác (HS bấm nhầm).
- Tab **Thống kê**: phân bố điểm, điểm TB/trung vị, **% đúng từng câu**, phân bố chọn phương án. Câu **dưới 30% đúng** được tô nổi — thường là đề/đáp án có vấn đề, nên kiểm tra lại.
- **Xuất Excel** (sheet Kết quả + sheet Thống kê câu) — thao tác được ghi vào nhật ký hệ thống.

---

## 6. Tổ chuyên môn

- **Tổ** (menu trái) — không gian chung của tổ: Hồ sơ tổ (nội dung phạm vi _Tổ_), thành viên, thông báo tổ.
- **Tổ trưởng / tổ phó** thêm được: duyệt GV xin vào tổ, mời thành viên (mời nhiều email một lúc — link để dán vào Zalo), đặt/bỏ tổ phó, ẩn/hiện nội dung của thành viên, thống kê tổ.
- Bạn được mời: mở link lời mời `/moi/…` → đăng nhập Google đúng email được mời → vào tổ ngay.
- **Gỡ khỏi tổ ≠ khóa tài khoản** — tài liệu của bạn vẫn thuộc tổ quản lý.

### Yêu thích

Thấy tài liệu/bài tập hay của đồng nghiệp → bấm **Lưu vào yêu thích** ở trang chi tiết → xem lại ở **Yêu thích**.

---

## 7. Câu hỏi thường gặp

**Học sinh báo "Không tìm thấy trang" khi bấm link bài tập?**
Kiểm tra: bài đã **Hiện** hoặc trong **khung hẹn giờ** chưa? Link có đúng không (đường dẫn dạng `/bai-tap/…-123`)? Bài phạm vi _Giáo viên/Tổ/Riêng tư_ thì khách không thấy là đúng.

**File .doc cũ không nạp được?**
Mở bằng Word → _Lưu thành → Word Document (.docx)_ rồi nạp lại.

**Đề có công thức MathType (ảnh lạ không hiển thị)?**
Ảnh MathType (WMF/EMF) chưa được hỗ trợ — thay bằng ảnh PNG trong editor, hệ thống sẽ cảnh báo vị trí cần thay.

**Muốn hủy một bài tập đã giao?**
Xóa bài giao ở tab **Bài đã giao** (lượt làm liên quan bị xóa — xác nhận kỹ) hoặc **Ẩn** bài tập + dời giờ đóng về trước.

**Muốn GV khác dùng bài tập của tôi?**
Đặt phạm vi **Công khai** (hoặc để tổ dùng bản nội bộ). Đồng nghiệp **nhân bản** về làm bản của họ hoặc **giao thẳng** cho lớp họ.

**Quên đã hẹn giờ lúc nào?**
Trên tab **Hiển thị** của bài tập có dòng mô tả bằng lời: "Đang mở đến 21:00 CN 11/10" — bấm **Hẹn giờ** để xem/sửa khung giờ.

# Định dạng file bài tập khi nhập vào hệ thống

Hệ thống nhận 2 định dạng: **Word (.docx)** và **Excel (.xlsx)**. Sau khi nhập, bài tập được tạo ở trạng thái **Ẩn** và chuyển sang màn hình rà soát để giáo viên kiểm tra trước khi hiển thị hoặc giao cho lớp.

Tải file mẫu: [mau-bai-tap.docx](/templates/mau-bai-tap.docx) · [mau-bai-tap.xlsx](/templates/mau-bai-tap.xlsx)

---

## 1. Word (.docx)

> Hệ thống chỉ nhận **.docx**. Nếu file đang ở dạng .doc: mở bằng Word → *File → Lưu thành → Word Document (.docx)* rồi tải lên lại.

### 1.1 Câu hỏi

- Câu hỏi bắt đầu dòng mới bằng `Câu <số>` + dấu `:`, `.` hoặc `)` (không phân biệt hoa thường).
- Cũng được chấp nhận: `Bài <số>`, hoặc đánh số thường `1.` / `1)` ở đầu dòng.

```text
Câu 1: Số thập phân gồm 5 đơn vị, 7 phần mười viết là:
Câu 2. Phân số nào bằng 3/4?
Câu 3) Trong các số sau, số nào nhỏ nhất?
```

### 1.2 Phương án

- Phương án bắt đầu bằng chữ cái `A.` `A)` `a)` … đến `H`.
- Nhiều phương án có thể nằm trên **một dòng**, cách nhau bằng tab hoặc ít nhất 2 dấu cách:

  ```text
  A. 5,07        *B. 5,7        C. 57        D. 0,57
  ```

- Phương án cũng có thể nằm **trong bảng** (ví dụ bảng 2×2) — hệ thống đọc theo hàng rồi theo ô.

### 1.3 Đánh dấu đáp án đúng

Có 4 cách, hệ thống ưu tiên theo thứ tự từ trên xuống:

| Ưu tiên | Cách | Ví dụ |
| :-----: | ---- | ----- |
| 1 | Dấu `*` ngay trước chữ cái phương án | `*B.` |
| 2 | Dòng `Đáp án: …` sau các phương án | `Đáp án: B` · nhiều đáp án: `Đáp án: A, C` |
| 3 | Khối đáp án cuối file sau tiêu đề `ĐÁP ÁN` / `BẢNG ĐÁP ÁN` | `1.C 2.B 3.AC` · hoặc `1-C; 2-B` · hoặc bảng 2 hàng (Câu / Đáp án) |
| 4 | Định dạng của phương án: **gạch chân**, hoặc **tô chữ đỏ**, hoặc là phương án **duy nhất in đậm** trong câu | — |

- Nếu nhiều nguồn cho kết quả **khác nhau** → hệ thống lấy nguồn ưu tiên cao hơn và **báo cảnh báo** để giáo viên kiểm tra.
- Câu có **nhiều hơn 1 đáp án đúng** → tự thành câu **chọn nhiều**.

### 1.4 Câu Đúng/Sai

Phương án là `Đúng` / `Sai` (có hoặc không kèm chữ cái):

```text
Câu 4: 0,5 = 1/2.
*Đúng
Sai
```

### 1.5 Giải thích

Dòng bắt đầu bằng `Giải thích:` / `Lời giải:` / `Hướng dẫn:` (hoặc `Hướng dẫn giải:`) ngay sau phương án/đáp án:

```text
Đáp án: A
Giải thích: Nhân cả tử và mẫu của 3/4 với 2 được 6/8.
```

### 1.6 Nhóm câu & đoạn văn (đọc hiểu)

- Dòng `PHẦN …` / `Phần …` mở **nhóm mới** (vd `PHẦN II. ĐỌC HIỂU`).
- Văn bản hoặc ảnh nằm giữa tiêu đề nhóm và câu hỏi đầu tiên của nhóm → trở thành **đoạn dẫn** (bài đọc) của nhóm.
- Dòng dạng `Đọc đoạn văn sau…` / `Quan sát hình…` / `Dựa vào… trả lời câu…` ở giữa bài cũng mở nhóm mới.
- Văn bản đứng **trước câu hỏi đầu tiên** (không thuộc nhóm nào) → thành **mô tả** của bài tập.

### 1.7 Ảnh & công thức

- Ảnh PNG/JPG/GIF trong đề và phương án được giữ nguyên.
- Công thức Word (Equation) đơn giản (phân số, lũy thừa, chỉ số) được chuyển thành văn bản.
- Ảnh công thức MathType (WMF/EMF) **không hiển thị được** — hệ thống báo cảnh báo, giáo viên thay bằng ảnh PNG trực tiếp trong editor.

### 1.8 Ví dụ hoàn chỉnh

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

### 1.9 Cảnh báo thường gặp

| Mã | Ý nghĩa |
| -- | ------- |
| `NO_CORRECT_ANSWER` | Câu chưa xác định được đáp án đúng — cần đánh dấu trong editor |
| `TOO_FEW_OPTIONS` | Câu có ít hơn 2 phương án |
| `EMPTY_OPTION` | Có phương án để trống |
| `DUPLICATE_OPTION_LABEL` | Phương án trùng chữ cái (hai phương án đều `A.`) |
| `QUESTION_NUMBER_GAP` | Nhảy số câu (vd câu 2 rồi câu 4) |
| `DUPLICATE_QUESTION_NUMBER` | Hai câu cùng số |
| `ANSWER_KEY_UNMATCHED` | Số câu trong khối ĐÁP ÁN không khớp với bài |
| `CONFLICTING_ANSWER_SOURCES` | Hai nguồn đáp án cho kết quả khác nhau |
| `UNSUPPORTED_IMAGE_FORMAT` | Ảnh không hiển thị được (thường là MathType) — thay bằng PNG |
| `EQUATION_SIMPLIFIED` | Công thức đã được chuyển thành văn bản — kiểm tra lại |

---

## 2. Excel (.xlsx)

Dùng sheet đầu tiên, **hàng 1 là tiêu đề cột**:

| Cột | Bắt buộc | Ví dụ |
| --- | :------: | ----- |
| STT | | 1 |
| **Câu hỏi** | ✓ | Số thập phân gồm 5 đơn vị, 7 phần mười viết là: |
| **A** … **F** | ≥ 2 cột có giá trị | 5,07 |
| **Đáp án** | ✓ | `B` · nhiều đáp án: `A,C` · câu Đúng/Sai: `Đúng` |
| Giải thích | | Nhân cả tử và mẫu với 2… |
| Nhóm | | PHẦN II. ĐỌC HIỂU |
| Đoạn văn | (chỉ cần ở dòng đầu của nhóm) | Mùa thu, bầu trời như cao hơn… |
| Điểm | (mặc định 1) | 1 |

Lưu ý:

- Giá trị đọc dưới dạng **chuỗi** — "5,7" không bị hiểu thành số.
- Cột nhóm: cùng tên nhóm = cùng nhóm; chỉ dòng đầu của nhóm cần điền Đoạn văn.
- Đáp án viết chữ cái phương án (vd `B` hoặc `A,C`), hoặc `Đúng` / `Sai` với câu Đúng/Sai.

---

## 3. Sau khi nhập

1. Bài tập được tạo ở trạng thái **Ẩn** — học sinh và khách chưa thấy.
2. Màn hình rà soát hiện số câu nhận diện và các câu cần kiểm tra (cảnh báo).
3. Giáo viên kiểm tra, sửa trực tiếp (đề, phương án, đáp án, giải thích, điểm, nhóm).
4. Chọn cách hiển thị: **Hiện ngay** · **Hẹn giờ** (vd cuối tuần) · hoặc **Giao cho lớp** bằng mã/QR.

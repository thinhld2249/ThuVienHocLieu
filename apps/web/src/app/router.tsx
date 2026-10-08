import { lazy } from "react";
import { createBrowserRouter } from "react-router";
import { PublicLayout } from "@/app/layouts/PublicLayout";
import { TeacherLayout } from "@/app/layouts/TeacherLayout";
import { AdminLayout } from "@/app/layouts/AdminLayout";
import { RequireActive, RequireAdmin } from "@/features/auth/guards";
import { HomePage } from "@/routes/public/home";
import { NotFoundPage } from "@/routes/public/not-found";

// spec §8.5: tách bundle /gv + /admin + các trang phụ; bundle công khai (home) nhẹ.
const TimKiemPage = lazy(() =>
  import("@/routes/public/tim-kiem").then((m) => ({ default: m.TimKiemPage })),
);
const TrangPage = lazy(() =>
  import("@/routes/public/trang").then((m) => ({ default: m.TrangPage })),
);
const ChuyenMucPage = lazy(() =>
  import("@/routes/public/chuyen-muc").then((m) => ({
    default: m.ChuyenMucPage,
  })),
);
const KhoiPage = lazy(() =>
  import("@/routes/public/khoi").then((m) => ({ default: m.KhoiPage })),
);
const TaiLieuPage = lazy(() =>
  import("@/routes/public/tai-lieu").then((m) => ({ default: m.TaiLieuPage })),
);
const BaiTapPage = lazy(() =>
  import("@/routes/public/bai-tap").then((m) => ({ default: m.BaiTapPage })),
);
const LamBaiPage = lazy(() =>
  import("@/routes/public/lam-bai").then((m) => ({ default: m.LamBaiPage })),
);
const VaoLopPage = lazy(() =>
  import("@/routes/public/vao-lop").then((m) => ({
    default: m.VaoLopPage,
  })),
);
const KetQuaPage = lazy(() =>
  import("@/routes/public/ket-qua").then((m) => ({ default: m.KetQuaPage })),
);
const DangNhapPage = lazy(() =>
  import("@/routes/public/dang-nhap").then((m) => ({
    default: m.DangNhapPage,
  })),
);
const ChoDuyetPage = lazy(() =>
  import("@/routes/public/cho-duyet").then((m) => ({
    default: m.ChoDuyetPage,
  })),
);
const MoiPage = lazy(() =>
  import("@/routes/public/moi").then((m) => ({ default: m.MoiPage })),
);
const GvPlaceholder = lazy(() =>
  import("@/routes/gv/placeholder").then((m) => ({
    default: m.GvPlaceholder,
  })),
);
const GvTaiLieuPage = lazy(() =>
  import("@/routes/gv/tai-lieu").then((m) => ({
    default: m.GvTaiLieuPage,
  })),
);
const TaiLieuFormPage = lazy(() =>
  import("@/routes/gv/tai-lieu-form").then((m) => ({
    default: m.TaiLieuFormPage,
  })),
);
const GvBaiTapPage = lazy(() =>
  import("@/routes/gv/bai-tap").then((m) => ({ default: m.GvBaiTapPage })),
);
const GvBaiTapFormPage = lazy(() =>
  import("@/routes/gv/bai-tap-form").then((m) => ({
    default: m.GvBaiTapFormPage,
  })),
);
const GvLopPage = lazy(() =>
  import("@/routes/gv/lop").then((m) => ({ default: m.GvLopPage })),
);
const GvLopIdPage = lazy(() =>
  import("@/routes/gv/lop-id").then((m) => ({
    default: m.GvLopIdPage,
  })),
);
const YeuThichPage = lazy(() =>
  import("@/routes/gv/yeu-thich").then((m) => ({
    default: m.YeuThichPage,
  })),
);
const ToPage = lazy(() =>
  import("@/routes/gv/to").then((m) => ({ default: m.ToPage })),
);
const GvHoSoPage = lazy(() =>
  import("@/routes/gv/ho-so").then((m) => ({
    default: m.GvHoSoPage,
  })),
);
const ToTeamPage = lazy(() =>
  import("@/routes/gv/to-team").then((m) => ({ default: m.ToTeamPage })),
);
const AdminToPage = lazy(() =>
  import("@/routes/admin/to").then((m) => ({ default: m.AdminToPage })),
);
const AdminLoiMoiPage = lazy(() =>
  import("@/routes/admin/loi-moi").then((m) => ({
    default: m.AdminLoiMoiPage,
  })),
);
// M6: khu Admin hoàn thiện (spec §5.4)
const AdminDashboardPage = lazy(() =>
  import("@/routes/admin/dashboard").then((m) => ({
    default: m.AdminDashboardPage,
  })),
);
const AdminUsersPage = lazy(() =>
  import("@/routes/admin/nguoi-dung").then((m) => ({
    default: m.AdminUsersPage,
  })),
);
const AdminContentPage = lazy(() =>
  import("@/routes/admin/noi-dung").then((m) => ({
    default: m.AdminContentPage,
  })),
);
const AdminReportsPage = lazy(() =>
  import("@/routes/admin/bao-cao").then((m) => ({
    default: m.AdminReportsPage,
  })),
);
const AdminTaxonomyPage = lazy(() =>
  import("@/routes/admin/danh-muc").then((m) => ({
    default: m.AdminTaxonomyPage,
  })),
);
const AdminClassesPage = lazy(() =>
  import("@/routes/admin/lop").then((m) => ({
    default: m.AdminClassesPage,
  })),
);
const AdminYearsPage = lazy(() =>
  import("@/routes/admin/nam-hoc").then((m) => ({
    default: m.AdminYearsPage,
  })),
);
const AdminAnnouncementsPage = lazy(() =>
  import("@/routes/admin/thong-bao").then((m) => ({
    default: m.AdminAnnouncementsPage,
  })),
);
const AdminSettingsPage = lazy(() =>
  import("@/routes/admin/cai-dat").then((m) => ({
    default: m.AdminSettingsPage,
  })),
);
const AdminAuditLogsPage = lazy(() =>
  import("@/routes/admin/nhat-ky").then((m) => ({
    default: m.AdminAuditLogsPage,
  })),
);
const AdminSystemPage = lazy(() =>
  import("@/routes/admin/he-thong").then((m) => ({
    default: m.AdminSystemPage,
  })),
);

export const router = createBrowserRouter([
  {
    path: "/",
    element: <PublicLayout />,
    errorElement: <NotFoundPage />,
    children: [
      { index: true, element: <HomePage /> },
      // route tiếng Việt không dấu (spec §0.4)
      { path: "tim-kiem", element: <TimKiemPage /> },
      { path: "chuyen-muc/:section", element: <ChuyenMucPage /> },
      // M3: duyệt theo khối + chi tiết tài liệu (spec §5.1)
      { path: "khoi/:grade", element: <KhoiPage /> },
      // URL dạng /{slug}-{id} (spec §8.4); RR7: dynamic segment = cả 1 segment
      // nên dùng 1 param, page tự tách id bằng regex.
      { path: "tai-lieu/:slugId", element: <TaiLieuPage /> },
      // M4: bài tập — giới thiệu, làm bài, kết quả (spec §5.1, §6.6–6.7)
      { path: "bai-tap/:slugId", element: <BaiTapPage /> },
      { path: "lam-bai/:attemptId", element: <LamBaiPage /> },
      { path: "ket-qua/:attemptId", element: <KetQuaPage /> },
      // M5: vào lớp làm bài bằng mã giao bài / QR (spec §5.1, §7)
      { path: "vao-lop", element: <VaoLopPage /> },
      { path: "trang/:slug", element: <TrangPage /> },
      // M1: xác thực & tài khoản (spec §3)
      { path: "dang-nhap", element: <DangNhapPage /> },
      { path: "cho-duyet", element: <ChoDuyetPage /> },
      { path: "moi/:token", element: <MoiPage /> },
    ],
  },
  {
    path: "/gv",
    element: (
      <RequireActive>
        <TeacherLayout />
      </RequireActive>
    ),
    children: [
      { index: true, element: <GvPlaceholder /> },
      // M3: tài liệu của tôi + form tạo/sửa + yêu thích (spec §5.2)
      { path: "tai-lieu", element: <GvTaiLieuPage /> },
      { path: "tai-lieu/moi", element: <TaiLieuFormPage /> },
      { path: "tai-lieu/:id", element: <TaiLieuFormPage /> },
      // M4: bài tập của tôi — danh sách + form (tabs câu hỏi/cài đặt/kết quả)
      { path: "bai-tap", element: <GvBaiTapPage /> },
      { path: "bai-tap/:id", element: <GvBaiTapFormPage /> },
      // M5: lớp học — danh sách + chi tiết (tabs HS/bài giao/bảng điểm)
      { path: "lop", element: <GvLopPage /> },
      { path: "lop/:id", element: <GvLopIdPage /> },
      { path: "yeu-thich", element: <YeuThichPage /> },
      // M2: không gian tổ (spec §5.2–5.3)
      { path: "to", element: <ToPage /> },
      { path: "to/:teamId", element: <ToTeamPage /> },
      // M1: hồ sơ + mật khẩu đăng nhập (spec §5.2)
      { path: "ho-so", element: <GvHoSoPage /> },
    ],
  },
  {
    path: "/admin",
    element: (
      <RequireAdmin>
        <AdminLayout />
      </RequireAdmin>
    ),
    children: [
      // M6: dashboard là trang chính (spec §5.4)
      { index: true, element: <AdminDashboardPage /> },
      // M2: tổ & lời mời (spec §5.4)
      { path: "to", element: <AdminToPage /> },
      { path: "loi-moi", element: <AdminLoiMoiPage /> },
      // M6: quản trị hoàn thiện (spec §5.4)
      { path: "nguoi-dung", element: <AdminUsersPage /> },
      { path: "noi-dung", element: <AdminContentPage /> },
      { path: "bao-cao", element: <AdminReportsPage /> },
      { path: "danh-muc", element: <AdminTaxonomyPage /> },
      { path: "lop", element: <AdminClassesPage /> },
      { path: "nam-hoc", element: <AdminYearsPage /> },
      { path: "thong-bao", element: <AdminAnnouncementsPage /> },
      { path: "cai-dat", element: <AdminSettingsPage /> },
      { path: "nhat-ky", element: <AdminAuditLogsPage /> },
      { path: "he-thong", element: <AdminSystemPage /> },
    ],
  },
  { path: "*", element: <NotFoundPage /> },
]);

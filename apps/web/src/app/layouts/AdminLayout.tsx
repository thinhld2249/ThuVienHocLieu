import { Suspense, useEffect } from "react";
import { Link, NavLink, Outlet, useLocation } from "react-router";
import { cn } from "@/lib/utils";
import { PageLoading } from "@/app/PageLoading";
import { SessionHeader } from "@/components/common/SessionHeader";
import { useMe } from "@/features/auth/api";

const nav = [
  { to: "/", label: "Trang chủ" },
  { to: "/admin", label: "Tổng quan", end: true },
  { to: "/admin/nguoi-dung", label: "Người dùng" },
  { to: "/admin/to", label: "Tổ" },
  { to: "/admin/loi-moi", label: "Lời mời" },
  { to: "/admin/noi-dung", label: "Nội dung" },
  { to: "/admin/bao-cao", label: "Báo cáo" },
  { to: "/admin/danh-muc", label: "Danh mục" },
  { to: "/admin/lop", label: "Lớp" },
  { to: "/admin/nam-hoc", label: "Năm học" },
  { to: "/admin/thong-bao", label: "Thông báo" },
  { to: "/admin/cai-dat", label: "Cài đặt" },
  { to: "/admin/nhat-ky", label: "Nhật ký" },
  { to: "/admin/he-thong", label: "Hệ thống" },
];

/** Khu Admin (spec §5.4, §14.3): sidebar trái + thanh phiên đầu trang. */
export function AdminLayout() {
  const { refetch: refetchMe } = useMe();
  const location = useLocation();

  // Làm mới me khi chuyển trang: tổ/vai trò cập nhật mà không cần F5.
  useEffect(() => {
    void refetchMe();
  }, [location.pathname, refetchMe]);

  return (
    <div className="flex min-h-dvh">
      <aside className="hidden w-56 shrink-0 border-r border-grid bg-white p-4 md:block">
        <Link to="/admin" className="mb-4 flex items-center gap-2 px-2">
          <img src="/favicon.svg" alt="" className="size-6" />
          <span className="font-semibold">Học Liệu · Quản trị</span>
        </Link>
        <nav className="space-y-1">
          {nav.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                cn(
                  "block rounded-btn px-3 py-2 text-sm text-muted transition hover:bg-paper hover:text-ink",
                  isActive && "bg-violet/10 font-medium text-violet",
                )
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <SessionHeader />
        <main className="min-w-0 flex-1 p-4 md:p-6">
          <Suspense fallback={<PageLoading />}>
            <Outlet />
          </Suspense>
        </main>
      </div>
    </div>
  );
}

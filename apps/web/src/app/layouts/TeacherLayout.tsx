import { Suspense, useEffect } from "react";
import { Link, NavLink, Outlet, useLocation } from "react-router";
import { cn } from "@/lib/utils";
import { PageLoading } from "@/app/PageLoading";
import { SessionHeader } from "@/components/common/SessionHeader";
import { useMe } from "@/features/auth/api";

const nav = [
  { to: "/", label: "Trang chủ" },
  { to: "/gv", label: "Tổng quan", end: true },
  { to: "/gv/tai-lieu", label: "Tài liệu" },
  { to: "/gv/bai-tap", label: "Bài tập" },
  { to: "/gv/lop", label: "Lớp" },
  { to: "/gv/to", label: "Tổ" },
  { to: "/gv/yeu-thich", label: "Yêu thích" },
];

/** Khu GV: sidebar trái + thanh phiên (chuông/đăng xuất) đầu trang (spec §14.3). */
export function TeacherLayout() {
  const { data: me, refetch: refetchMe } = useMe();
  const location = useLocation();

  // Làm mới me khi chuyển trang: tổ/vai trò cập nhật (mời, bổ nhiệm) mà không cần F5.
  useEffect(() => {
    void refetchMe();
  }, [location.pathname, refetchMe]);

  return (
    <div className="flex min-h-dvh">
      <aside className="hidden w-56 shrink-0 border-r border-grid bg-white p-4 md:block">
        <Link to="/gv" className="mb-4 flex items-center gap-2 px-2">
          <img src="/favicon.svg" alt="" className="size-6" />
          <span className="font-semibold">Học Liệu · GV</span>
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
          {me?.systemRole === "Admin" && (
            <NavLink
              to="/admin"
              className={({ isActive }) =>
                cn(
                  "mt-2 block rounded-btn border-t border-grid px-3 py-2 pt-3 text-sm font-medium text-violet transition hover:bg-violet/10",
                  isActive && "bg-violet/10",
                )
              }
            >
              Quản trị
            </NavLink>
          )}
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

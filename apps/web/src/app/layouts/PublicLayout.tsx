import { Suspense, useEffect } from "react";
import { Link, Outlet, useLocation, useNavigate } from "react-router";
import { LogOut, Search } from "lucide-react";
import { useLogout, useMe } from "@/features/auth/api";
import { PageLoading } from "@/app/PageLoading";

export function PublicLayout() {
  const navigate = useNavigate();
  const { refetch: refetchMe } = useMe();
  const location = useLocation();

  // Làm mới me khi chuyển trang: tài khoản Pending được duyệt → Active không cần F5.
  useEffect(() => {
    void refetchMe();
  }, [location.pathname, refetchMe]);

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="sticky top-0 z-40 border-b border-grid bg-paper/95 backdrop-blur">
        <div className="mx-auto flex h-14 w-full max-w-6xl items-center gap-4 px-4">
          <Link to="/" className="flex shrink-0 items-center gap-2">
            <img src="/favicon.svg" alt="" className="size-7" />
            <span className="text-lg font-semibold tracking-tight">
              Học Liệu
            </span>
          </Link>
          <form
            className="ml-auto hidden w-full max-w-md sm:block"
            onSubmit={(e) => {
              e.preventDefault();
              const q = new FormData(e.currentTarget)
                .get("q")
                ?.toString()
                .trim();
              navigate(
                q ? `/tim-kiem?q=${encodeURIComponent(q)}` : "/tim-kiem",
              );
            }}
            role="search"
          >
            <label htmlFor="site-search" className="sr-only">
              Tìm tài liệu, bài tập…
            </label>
            <div className="relative">
              <input
                id="site-search"
                name="q"
                type="search"
                placeholder="Tìm tài liệu, bài tập…"
                className="w-full rounded-btn border border-grid bg-white py-2 pl-3 pr-10 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
              />
              <button
                type="submit"
                aria-label="Tìm kiếm"
                className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-muted transition hover:text-violet"
              >
                <Search className="size-4" />
              </button>
            </div>
          </form>
          <LoginChip />
        </div>
      </header>

      <main className="flex-1">
        <Suspense fallback={<PageLoading />}>
          <Outlet />
        </Suspense>
      </main>

      <footer className="border-t border-grid bg-white">
        <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-6 text-sm text-muted">
          <span className="font-medium text-ink">Học Liệu</span>
          <Link to="/trang/gioi-thieu" className="transition hover:text-violet">
            Giới thiệu
          </Link>
          <Link to="/trang/huong-dan" className="transition hover:text-violet">
            Hướng dẫn
          </Link>
          <Link
            to="/trang/chinh-sach-du-lieu"
            className="transition hover:text-violet"
          >
            Chính sách dữ liệu
          </Link>
        </div>
      </footer>
    </div>
  );
}

/** M1: chip trạng thái đăng nhập — khách thấy nút "Đăng nhập", GV thấy avatar + tên. */
function LoginChip() {
  const { data: me, isPending } = useMe();
  const { mutateAsync: logout } = useLogout();
  const navigate = useNavigate();

  if (isPending) return null;

  if (!me)
    return (
      <Link
        to="/dang-nhap"
        className="shrink-0 rounded-btn border border-violet/40 px-3 py-1.5 text-sm font-medium text-violet transition hover:bg-violet/10"
      >
        Đăng nhập
      </Link>
    );

  const dest = me.status === "Pending" ? "/cho-duyet" : "/gv";
  return (
    <div className="flex shrink-0 items-center gap-2">
      <Link
        to={dest}
        className="flex items-center gap-2 rounded-btn border border-grid bg-white py-1 pl-1 pr-3 text-sm transition hover:border-violet/40"
      >
        {me.avatarUrl ? (
          <img
            src={me.avatarUrl}
            alt=""
            className="size-7 rounded-full"
            referrerPolicy="no-referrer"
          />
        ) : (
          <span className="flex size-7 items-center justify-center rounded-full bg-violet/10 text-xs font-semibold text-violet">
            {me.fullName.charAt(0).toUpperCase()}
          </span>
        )}
        <span className="hidden max-w-32 truncate font-medium sm:inline">
          {me.fullName}
        </span>
        {me.status === "Pending" && (
          <span className="rounded-full bg-warn/15 px-2 py-0.5 text-xs text-warn">
            Chờ duyệt
          </span>
        )}
      </Link>
      <button
        type="button"
        onClick={() => {
          // Chờ cookie phiên bị xoá rồi mới chuyển trang — tránh refetch /me
          // chạy song song với cookie còn hiệu lực → "vẫn đăng nhập".
          void logout().finally(() => navigate("/"));
        }}
        aria-label="Đăng xuất"
        title="Đăng xuất"
        className="flex size-8 items-center justify-center rounded-btn text-muted transition hover:bg-paper hover:text-ink"
      >
        <LogOut className="size-4" />
      </button>
    </div>
  );
}

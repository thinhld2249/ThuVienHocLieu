import { LogOut } from "lucide-react";
import { Link, useNavigate } from "react-router";
import { NotificationBell } from "./NotificationBell";
import { useLogout, useMe } from "@/features/auth/api";

/**
 * Thanh đầu trang khu GV/Admin — nhãn tài khoản + chuông thông báo + đăng xuất
 * ở góc phải trên cùng (đúng vị trí như chip đăng nhập ở khu công khai).
 *
 * Đăng xuất chờ request xoá phiên xong mới về trang chủ — tránh refetch /me
 * chạy song song với cookie còn hiệu lực → "vẫn đăng nhập".
 */
export function SessionHeader() {
  const { data: me } = useMe();
  const { mutateAsync: logout } = useLogout();
  const navigate = useNavigate();

  return (
    <header className="sticky top-0 z-40 flex h-14 shrink-0 items-center justify-end gap-1 border-b border-grid bg-paper/95 px-4 backdrop-blur">
      {me && (
        <Link
          to="/gv/ho-so"
          title="Hồ sơ"
          className="mr-1 flex items-center gap-2 rounded-btn border border-grid bg-white py-1 pl-1 pr-3 text-sm transition hover:border-violet/40"
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
        </Link>
      )}
      <NotificationBell />
      {me && (
        <button
          type="button"
          onClick={() => {
            void logout().finally(() => navigate("/"));
          }}
          aria-label="Đăng xuất"
          title="Đăng xuất"
          className="flex size-10 items-center justify-center rounded-btn text-muted transition hover:bg-paper hover:text-ink"
        >
          <LogOut className="size-5" />
        </button>
      )}
    </header>
  );
}

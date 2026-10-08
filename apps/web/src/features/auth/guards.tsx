import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { Loader2 } from "lucide-react";
import { useLogout, useMe } from "./api";
import { Button } from "@/components/ui/button";

/** `from` chỉ nhận đường dẫn nội bộ — chống open redirect. */
function safeFrom(pathname: string, search: string): string {
  const from = new URLSearchParams(search).get("from");
  if (from && from.startsWith("/") && !from.startsWith("//")) return from;
  return pathname + search;
}

function FullScreen({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-4 bg-paper p-6 text-center">
      {children}
    </div>
  );
}

function Loading() {
  return (
    <FullScreen>
      <Loader2 className="size-6 animate-spin text-violet" aria-hidden />
      <p className="text-sm text-muted">Đang tải…</p>
    </FullScreen>
  );
}

/** Đã đăng nhập (mọi trạng thái); chưa đăng nhập → /dang-nhap?from=… */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { data: me, isPending } = useMe();
  const location = useLocation();
  if (isPending) return <Loading />;
  if (!me)
    return (
      <Navigate
        to={`/dang-nhap?from=${encodeURIComponent(safeFrom(location.pathname, location.search))}`}
        replace
      />
    );
  return <>{children}</>;
}

/** GV Active (hoặc Admin); Pending → /cho-duyet. */
export function RequireActive({ children }: { children: ReactNode }) {
  const { data: me, isPending } = useMe();
  const location = useLocation();
  if (isPending) return <Loading />;
  if (!me)
    return (
      <Navigate
        to={`/dang-nhap?from=${encodeURIComponent(safeFrom(location.pathname, location.search))}`}
        replace
      />
    );
  if (me.status === "Pending") return <Navigate to="/cho-duyet" replace />;
  if (me.status !== "Active" && me.systemRole !== "Admin")
    return (
      <FullScreen>
        <p className="max-w-md text-sm text-ink">
          Tài khoản của bạn đang{" "}
          {me.status === "Suspended" ? "bị khóa" : "không hoạt động"}.
        </p>
        {me.statusReason && (
          <p className="max-w-md text-sm text-muted">{me.statusReason}</p>
        )}
        <LogoutButton />
      </FullScreen>
    );
  return <>{children}</>;
}

/** Chỉ Admin (spec §2.2). */
export function RequireAdmin({ children }: { children: ReactNode }) {
  const { data: me, isPending } = useMe();
  if (isPending) return <Loading />;
  if (!me) return <Navigate to="/dang-nhap" replace />;
  if (me.systemRole !== "Admin")
    return (
      <FullScreen>
        <p className="text-sm text-ink">
          Trang này chỉ dành cho quản trị viên.
        </p>
        <Button
          variant="outline"
          onClick={() => (window.location.href = "/gv")}
        >
          Về khu giáo viên
        </Button>
      </FullScreen>
    );
  return <>{children}</>;
}

function LogoutButton() {
  const { mutate: logout } = useLogout();
  return (
    <Button variant="outline" onClick={() => logout()}>
      Đăng xuất
    </Button>
  );
}

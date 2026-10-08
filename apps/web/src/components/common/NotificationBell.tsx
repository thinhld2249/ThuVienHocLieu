import { Bell, CheckCheck } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { cn } from "@/lib/utils";
import { formatDateTime } from "@/lib/date";
import {
  useMarkNotificationsRead,
  useNotifications,
} from "@/features/teams/api";

/**
 * Chuông thông báo in-app (M2): poll /api/me/notifications 30s,
 * đánh dấu đã đọc toàn bộ hoặc từng tin.
 */
export function NotificationBell({ className }: { className?: string }) {
  const [open, setOpen] = useState(false);
  const boxRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();
  const { data, isPending } = useNotifications();
  const markRead = useMarkNotificationsRead();

  useEffect(() => {
    if (!open) return;
    function onDown(e: MouseEvent) {
      if (boxRef.current && !boxRef.current.contains(e.target as Node))
        setOpen(false);
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const unread = data?.unreadCount ?? 0;

  return (
    <div ref={boxRef} className={cn("relative", className)}>
      <button
        type="button"
        aria-label={`Thông báo${unread > 0 ? ` — ${unread} chưa đọc` : ""}`}
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        className="relative inline-flex size-10 items-center justify-center rounded-btn text-muted transition hover:bg-paper hover:text-ink"
      >
        <Bell className="size-5" aria-hidden />
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 inline-flex min-w-5 items-center justify-center rounded-full bg-redpen px-1 text-[11px] font-semibold text-white">
            {unread > 99 ? "99+" : unread}
          </span>
        )}
      </button>

      {open && (
        <div
          role="dialog"
          aria-label="Thông báo của tôi"
          className="absolute right-0 top-12 z-50 w-80 overflow-hidden rounded-card border border-grid bg-white shadow-lg"
        >
          <div className="flex items-center justify-between border-b border-grid px-4 py-2">
            <p className="text-sm font-medium">Thông báo</p>
            {unread > 0 && (
              <button
                type="button"
                onClick={() => markRead.mutate()}
                disabled={markRead.isPending}
                className="inline-flex items-center gap-1 text-xs text-violet hover:underline"
              >
                <CheckCheck className="size-3.5" aria-hidden /> Đánh dấu đã đọc
              </button>
            )}
          </div>
          <ul className="max-h-80 overflow-y-auto">
            {isPending && (
              <li className="px-4 py-6 text-center text-sm text-muted">
                Đang tải…
              </li>
            )}
            {!isPending && (data?.items.length ?? 0) === 0 && (
              <li className="px-4 py-6 text-center text-sm text-muted">
                Chưa có thông báo nào.
              </li>
            )}
            {data?.items.map((n) => (
              <li key={n.id} className="border-b border-grid/60 last:border-0">
                <button
                  type="button"
                  className={cn(
                    "block w-full px-4 py-3 text-left transition hover:bg-paper",
                    !n.read && "bg-violet/5",
                  )}
                  onClick={() => {
                    markRead.mutate([n.id]);
                    setOpen(false);
                    if (n.link) navigate(n.link);
                  }}
                >
                  <p
                    className={cn(
                      "text-sm",
                      !n.read && "font-medium text-ink",
                      n.read && "text-muted",
                    )}
                  >
                    {n.title}
                  </p>
                  <p className="mt-0.5 text-xs text-muted/80">
                    {formatDateTime(n.createdAt)}
                  </p>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

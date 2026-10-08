import { useState } from "react";
import { Download, ScrollText } from "lucide-react";
import { useAdminAuditLogs } from "@/features/admin/api";
import { EmptyState } from "@/components/common/EmptyState";
import { Pagination } from "@/components/common/Pagination";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";

/** /admin/nhat-ky — audit log: lọc, xem, xuất CSV (spec §5.4). */
export function AdminAuditLogsPage() {
  const [action, setAction] = useState("");
  const [actorUserId, setActorUserId] = useState("");
  const [page, setPage] = useState(1);

  const { data: paged, isPending } = useAdminAuditLogs({
    action: action || undefined,
    actorUserId: actorUserId ? Number(actorUserId) : undefined,
    page,
    pageSize: 50,
  });

  const csvQuery = new URLSearchParams();
  if (action) csvQuery.set("action", action);
  if (actorUserId) csvQuery.set("actorUserId", actorUserId);
  const csvUrl =
    "/api/admin/audit-logs.csv" +
    (csvQuery.toString() ? `?${csvQuery.toString()}` : "");

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Nhật ký hệ thống</h1>
        <div className="flex flex-wrap items-center gap-2">
          <input
            type="search"
            value={action}
            onChange={(e) => {
              setAction(e.target.value);
              setPage(1);
            }}
            placeholder="Hành động (VD: content.publish)"
            className="h-10 w-56 rounded-btn border border-grid bg-white px-3 text-sm outline-none focus:border-violet"
          />
          <input
            type="number"
            value={actorUserId}
            onChange={(e) => {
              setActorUserId(e.target.value);
              setPage(1);
            }}
            placeholder="ID người dùng"
            className="h-10 w-32 rounded-btn border border-grid bg-white px-3 text-sm outline-none focus:border-violet"
          />
          <a href={csvUrl}>
            <Button size="sm" variant="outline">
              <Download className="size-3.5" aria-hidden /> Xuất CSV
            </Button>
          </a>
        </div>
      </div>

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 8 }, (_, i) => (
            <Skeleton key={i} className="h-10" />
          ))}
        </div>
      ) : (paged?.items ?? []).length === 0 ? (
        <EmptyState
          icon={ScrollText}
          title="Chưa có sự kiện nào"
          description="Đăng nhập, duyệt tài khoản, ẩn/hiện, xóa, xuất kết quả… đều được ghi ở đây."
        />
      ) : (
        <>
          <div className="overflow-x-auto rounded-card border border-grid bg-white">
            <table className="w-full min-w-[880px] text-sm">
              <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
                <tr>
                  <th className="p-2">Thời gian</th>
                  <th className="p-2">Người thực hiện</th>
                  <th className="p-2">Hành động</th>
                  <th className="p-2">Đối tượng</th>
                  <th className="p-2">IP</th>
                  <th className="p-2">Chi tiết</th>
                </tr>
              </thead>
              <tbody>
                {(paged?.items ?? []).map((log) => (
                  <tr
                    key={log.id}
                    className="border-b border-grid/60 last:border-0 hover:bg-paper/50"
                  >
                    <td className="whitespace-nowrap p-2 text-muted">
                      {formatDateTime(log.createdAt)}
                    </td>
                    <td className="p-2">
                      {log.actorName ?? "Hệ thống"}
                      {log.actorUserId != null ? (
                        <span className="ml-1 text-xs text-muted">
                          (#{log.actorUserId})
                        </span>
                      ) : null}
                    </td>
                    <td className="p-2 font-mono text-xs">{log.action}</td>
                    <td className="p-2 text-muted">
                      {log.entityType
                        ? `${log.entityType}${log.entityId ? ` #${log.entityId}` : ""}`
                        : "—"}
                    </td>
                    <td className="p-2 text-muted">{log.ip ?? "—"}</td>
                    <td className="max-w-72 p-2">
                      <span
                        className="line-clamp-2 break-all font-mono text-xs text-muted"
                        title={log.data ?? undefined}
                      >
                        {log.data ?? "—"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={paged?.page ?? 1}
            pageSize={50}
            total={paged?.total ?? 0}
            onChange={setPage}
          />
        </>
      )}
    </div>
  );
}

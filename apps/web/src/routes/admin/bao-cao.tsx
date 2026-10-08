import { useState } from "react";
import { Flag } from "lucide-react";
import { useAdminPatchReport, useAdminReports } from "@/features/admin/api";
import { EmptyState } from "@/components/common/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";

/** /admin/bao-cao — báo lỗi/vi phạm từ người xem (spec §5.4). */
export function AdminReportsPage() {
  const [status, setStatus] = useState("Open");
  const { data: paged, isPending } = useAdminReports(status || undefined);
  const patchReport = useAdminPatchReport();

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Báo cáo</h1>
        <select
          aria-label="Lọc theo trạng thái"
          value={status}
          onChange={(e) => setStatus(e.target.value)}
          className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
        >
          <option value="Open">Đang mở</option>
          <option value="Resolved">Đã xử lý</option>
          <option value="Dismissed">Đã bỏ qua</option>
          <option value="">Tất cả</option>
        </select>
      </div>

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-14" />
          ))}
        </div>
      ) : (paged?.items ?? []).length === 0 ? (
        <EmptyState
          icon={Flag}
          title="Không có báo cáo"
          description="Báo lỗi/vi phạm mà người xem gửi sẽ hiển thị ở đây."
        />
      ) : (
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[880px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="p-2">Nội dung</th>
                <th className="p-2">Lý do</th>
                <th className="p-2">Chi tiết</th>
                <th className="p-2">Người báo</th>
                <th className="p-2">Thời gian</th>
                <th className="p-2">Trạng thái</th>
                <th className="p-2 text-right">Xử lý</th>
              </tr>
            </thead>
            <tbody>
              {(paged?.items ?? []).map((r) => (
                <tr
                  key={r.id}
                  className="border-b border-grid/60 last:border-0 hover:bg-paper/50"
                >
                  <td className="max-w-56 p-2">
                    <div className="truncate font-medium" title={r.itemTitle ?? undefined}>
                      {r.itemTitle ?? `#${r.itemId}`}
                    </div>
                    <div className="text-xs text-muted">
                      {r.itemType === "quiz" ? "Bài tập" : "Tài liệu"}
                    </div>
                  </td>
                  <td className="max-w-48 p-2">
                    <span className="line-clamp-2">{r.reason}</span>
                  </td>
                  <td className="max-w-56 p-2 text-muted">
                    <span className="line-clamp-2">{r.detail ?? "—"}</span>
                  </td>
                  <td className="p-2 text-muted">{r.reporterName ?? "Khách"}</td>
                  <td className="p-2 text-muted">
                    {formatDateTime(r.createdAt)}
                  </td>
                  <td className="p-2">
                    <ReportStatusBadge status={r.status} />
                    {r.note ? (
                      <div className="mt-0.5 max-w-40 text-xs text-muted">
                        {r.note}
                      </div>
                    ) : null}
                  </td>
                  <td className="p-2">
                    <div className="flex justify-end gap-1">
                      {r.status === "Open" ? (
                        <>
                          <Button
                            size="sm"
                            disabled={patchReport.isPending}
                            onClick={() =>
                              patchReport.mutate({
                                id: r.id,
                                body: { status: "Resolved" },
                              })
                            }
                          >
                            Xử lý
                          </Button>
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={patchReport.isPending}
                            onClick={() => {
                              const note = window.prompt("Ghi chú (tùy chọn):");
                              if (note === null) return;
                              patchReport.mutate({
                                id: r.id,
                                body: {
                                  status: "Dismissed",
                                  note: note.trim() || undefined,
                                },
                              });
                            }}
                          >
                            Bỏ qua
                          </Button>
                        </>
                      ) : (
                        <span className="text-xs text-muted">Đóng</span>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function ReportStatusBadge({ status }: { status: string }) {
  if (status === "Open")
    return <Badge className="bg-warn/10 text-warn">Đang mở</Badge>;
  if (status === "Resolved")
    return <Badge className="bg-correct/10 text-correct">Đã xử lý</Badge>;
  return <Badge className="bg-muted/10 text-muted">Đã bỏ qua</Badge>;
}

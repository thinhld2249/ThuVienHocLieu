import { useState } from "react";
import { Search, UserCog } from "lucide-react";
import {
  useAdminLogoutAll,
  useAdminPatchUser,
  useAdminTransferContent,
  useAdminUsersList,
} from "@/features/admin/api";
import type { AdminUserDetail, UserPatch } from "@/features/admin/types";
import { EmptyState } from "@/components/common/EmptyState";
import { Pagination } from "@/components/common/Pagination";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";
import { USER_STATUS_LABEL } from "./helpers";

/** /admin/nguoi-dung — bảng user: lọc, duyệt/khóa, cấp quyền, chuyển nội dung (spec §5.4). */
export function AdminUsersPage() {
  const [status, setStatus] = useState("");
  const [systemRole, setSystemRole] = useState("");
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const [transferFor, setTransferFor] = useState<number | null>(null);
  const [transferTo, setTransferTo] = useState(0);

  const { data: paged, isPending } = useAdminUsersList({
    status: status || undefined,
    systemRole: systemRole || undefined,
    q: q || undefined,
    page,
    pageSize: 20,
  });
  const { data: activeUsers } = useAdminUsersList({
    status: "Active",
    page: 1,
    pageSize: 100,
  });
  const patchUser = useAdminPatchUser();
  const transfer = useAdminTransferContent();
  const logoutAll = useAdminLogoutAll();

  const busy = patchUser.isPending || transfer.isPending || logoutAll.isPending;

  const act = (id: number, body: UserPatch) => patchUser.mutate({ id, body });

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Người dùng</h1>
        <div className="flex flex-wrap items-center gap-2">
          <select
            aria-label="Lọc theo trạng thái"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
            className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value="">Mọi trạng thái</option>
            <option value="Pending">Chờ duyệt</option>
            <option value="Active">Hoạt động</option>
            <option value="Suspended">Đang khóa</option>
            <option value="Rejected">Đã từ chối</option>
          </select>
          <select
            aria-label="Lọc theo vai trò"
            value={systemRole}
            onChange={(e) => {
              setSystemRole(e.target.value);
              setPage(1);
            }}
            className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value="">Mọi vai trò</option>
            <option value="Teacher">Giáo viên</option>
            <option value="Admin">Admin</option>
          </select>
          <div className="relative">
            <Search
              className="absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted"
              aria-hidden
            />
            <input
              type="search"
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                setPage(1);
              }}
              placeholder="Tìm tên, email…"
              className="h-10 w-52 rounded-btn border border-grid bg-white pl-8 pr-3 text-sm outline-none focus:border-violet"
            />
          </div>
        </div>
      </div>

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : (paged?.items ?? []).length === 0 ? (
        <EmptyState
          icon={UserCog}
          title="Không có người dùng phù hợp"
          description="Thử đổi bộ lọc hoặc từ khóa tìm kiếm."
        />
      ) : (
        <>
          <div className="overflow-x-auto rounded-card border border-grid bg-white">
            <table className="w-full min-w-[980px] text-sm">
              <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
                <tr>
                  <th className="p-2">Giáo viên</th>
                  <th className="p-2">SĐT</th>
                  <th className="p-2">Vai trò</th>
                  <th className="p-2">Trạng thái</th>
                  <th className="p-2">Tổ</th>
                  <th className="p-2">Đăng nhập cuối</th>
                  <th className="p-2 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {(paged?.items ?? []).map((u) => (
                  <UserRow
                    key={u.id}
                    user={u}
                    busy={busy}
                    act={act}
                    onLogout={() => {
                      if (
                        window.confirm(
                          `Đặt "${u.fullName}" đăng xuất trên mọi thiết bị?`,
                        )
                      )
                        void logoutAll.mutateAsync(u.id);
                    }}
                    transferOpen={transferFor === u.id}
                    transferTo={transferTo}
                    onTransferOpen={(open) => {
                      setTransferFor(open ? u.id : null);
                      setTransferTo(0);
                    }}
                    onTransferToChange={setTransferTo}
                    candidates={(activeUsers?.items ?? []).filter(
                      (c) => c.id !== u.id,
                    )}
                    onTransferConfirm={() => {
                      if (transferTo === 0) return;
                      void transfer
                        .mutateAsync({ fromUserId: u.id, toUserId: transferTo })
                        .then(() => {
                          setTransferFor(null);
                          setTransferTo(0);
                        });
                    }}
                  />
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={paged?.page ?? 1}
            pageSize={20}
            total={paged?.total ?? 0}
            onChange={setPage}
          />
        </>
      )}
    </div>
  );
}

function UserRow({
  user: u,
  busy,
  act,
  onLogout,
  transferOpen,
  transferTo,
  onTransferOpen,
  onTransferToChange,
  candidates,
  onTransferConfirm,
}: {
  user: AdminUserDetail;
  busy: boolean;
  act: (id: number, body: UserPatch) => void;
  onLogout: () => void;
  transferOpen: boolean;
  transferTo: number;
  onTransferOpen: (open: boolean) => void;
  onTransferToChange: (id: number) => void;
  candidates: AdminUserDetail[];
  onTransferConfirm: () => void;
}) {
  return (
    <>
      <tr className="border-b border-grid/60 last:border-0 hover:bg-paper/50">
        <td className="p-2">
          <div className="font-medium">{u.fullName}</div>
          <div className="text-xs text-muted">{u.email}</div>
          {u.statusReason ? (
            <div className="text-xs text-wrong">{u.statusReason}</div>
          ) : null}
        </td>
        <td className="p-2 text-muted">{u.phone ?? "—"}</td>
        <td className="p-2">
          {u.systemRole === "Admin" ? (
            <Badge className="bg-violet/10 text-violet">Admin</Badge>
          ) : (
            <span className="text-muted">Giáo viên</span>
          )}
        </td>
        <td className="p-2">
          <StatusBadge status={u.status} />
          {u.requestedTeamName && u.status === "Pending" ? (
            <div className="mt-0.5 text-xs text-muted">
              Xin vào: {u.requestedTeamName}
            </div>
          ) : null}
        </td>
        <td className="p-2 text-muted">
          {u.teams.length > 0 ? u.teams.map((t) => t.teamName).join(", ") : "—"}
        </td>
        <td className="p-2 text-muted">
          {u.lastLoginAt ? formatDateTime(u.lastLoginAt) : "Chưa"}
        </td>
        <td className="p-2">
          <div className="flex flex-wrap justify-end gap-1">
            {u.status === "Pending" ? (
              <>
                <Button
                  size="sm"
                  disabled={busy}
                  onClick={() =>
                    act(u.id, {
                      status: "Active",
                      teamIds:
                        u.requestedTeamId != null ? [u.requestedTeamId] : [],
                    })
                  }
                >
                  Duyệt
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={busy}
                  onClick={() => {
                    const reason = window.prompt("Lý do từ chối (tùy chọn):");
                    if (reason === null) return;
                    act(u.id, {
                      status: "Rejected",
                      statusReason: reason.trim() || null,
                    });
                  }}
                >
                  Từ chối
                </Button>
              </>
            ) : null}
            {u.status === "Active" ? (
              <Button
                size="sm"
                variant="outline"
                disabled={busy}
                onClick={() => {
                  if (
                    window.confirm(
                      `Khóa tài khoản "${u.fullName}"? Người dùng sẽ không đăng nhập được.`,
                    )
                  )
                    act(u.id, { status: "Suspended" });
                }}
              >
                Khóa
              </Button>
            ) : null}
            {u.status === "Suspended" ? (
              <Button
                size="sm"
                disabled={busy}
                onClick={() => act(u.id, { status: "Active" })}
              >
                Mở khóa
              </Button>
            ) : null}
            {u.status === "Rejected" ? (
              <Button
                size="sm"
                disabled={busy}
                onClick={() => act(u.id, { status: "Pending" })}
              >
                Mở lại
              </Button>
            ) : null}
            <Button
              size="sm"
              variant="outline"
              disabled={busy}
              onClick={() =>
                act(u.id, {
                  systemRole: u.systemRole === "Admin" ? "Teacher" : "Admin",
                })
              }
            >
              {u.systemRole === "Admin" ? "Thu Admin" : "Cấp Admin"}
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={busy}
              onClick={() => onTransferOpen(!transferOpen)}
            >
              Chuyển nội dung
            </Button>
            <Button
              size="sm"
              variant="ghost"
              disabled={busy}
              onClick={onLogout}
            >
              ĐX mọi thiết bị
            </Button>
          </div>
        </td>
      </tr>
      {transferOpen ? (
        <tr className="border-b border-grid/60 bg-paper/60 last:border-0">
          <td colSpan={7} className="p-2">
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-muted">
                Chuyển toàn bộ tài liệu &amp; bài tập của {u.fullName} sang:
              </span>
              <select
                aria-label="GV nhận nội dung"
                value={transferTo}
                onChange={(e) => onTransferToChange(Number(e.target.value))}
                className="h-9 max-w-64 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
              >
                <option value={0}>Chọn giáo viên nhận…</option>
                {candidates.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.fullName} ({c.email})
                  </option>
                ))}
              </select>
              <Button
                size="sm"
                disabled={transferTo === 0 || busy}
                onClick={onTransferConfirm}
              >
                Xác nhận
              </Button>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => onTransferOpen(false)}
              >
                Hủy
              </Button>
            </div>
          </td>
        </tr>
      ) : null}
    </>
  );
}

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Active"
      ? "bg-correct/10 text-correct"
      : status === "Pending"
        ? "bg-warn/10 text-warn"
        : status === "Suspended"
          ? "bg-wrong/10 text-wrong"
          : "bg-muted/10 text-muted";
  return <Badge className={cls}>{USER_STATUS_LABEL[status] ?? status}</Badge>;
}

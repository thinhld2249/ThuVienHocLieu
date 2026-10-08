import type { ReactNode } from "react";
import { Link } from "react-router";
import { AlertTriangle, Check, FileWarning, X } from "lucide-react";
import {
  useAdminDashboard,
  useAdminPatchUser,
  useAdminRetryFile,
} from "@/features/admin/api";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";
import { formatBytes } from "./helpers";

/** /admin — dashboard: hàng chờ duyệt, cảnh báo, số liệu vận hành (spec §5.4). */
export function AdminDashboardPage() {
  const { data, isPending } = useAdminDashboard();
  const patchUser = useAdminPatchUser();
  const retryFile = useAdminRetryFile();

  if (isPending) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} className="h-28" />
        ))}
      </div>
    );
  }
  if (!data) return null;

  const approve = (u: (typeof data.approvalQueue)[number]) =>
    patchUser.mutate({
      id: u.userId,
      body: {
        status: "Active",
        teamIds: u.requestedTeamId != null ? [u.requestedTeamId] : [],
      },
    });
  const reject = (u: (typeof data.approvalQueue)[number]) => {
    const reason = window.prompt("Lý do từ chối (tùy chọn):");
    if (reason === null) return;
    patchUser.mutate({
      id: u.userId,
      body: { status: "Rejected", statusReason: reason.trim() || null },
    });
  };

  return (
    <div className="space-y-5">
      <h1 className="text-xl font-semibold">Tổng quan</h1>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label="Chờ duyệt"
          value={data.users.pending}
          to="/admin/nguoi-dung"
        />
        <StatCard label="Giáo viên hoạt động" value={data.users.active} />
        <StatCard label="Đang khóa" value={data.users.suspended} />
        <StatCard label="Đã từ chối" value={data.users.rejected} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card
          title="Hàng chờ duyệt"
          action={
            <Link
              to="/admin/nguoi-dung"
              className="text-sm text-violet hover:underline"
            >
              Xem tất cả
            </Link>
          }
        >
          {data.approvalQueue.length === 0 ? (
            <p className="text-sm text-muted">Không có giáo viên chờ duyệt.</p>
          ) : (
            <ul className="space-y-2">
              {data.approvalQueue.map((u) => (
                <li
                  key={u.userId}
                  className="flex items-center justify-between gap-2 rounded-btn border border-grid/60 p-2"
                >
                  <div className="min-w-0">
                    <div className="truncate text-sm font-medium">
                      {u.fullName}
                    </div>
                    <div className="truncate text-xs text-muted">
                      {u.email}
                      {u.requestedTeamName
                        ? ` · xin vào ${u.requestedTeamName}`
                        : ""}
                    </div>
                  </div>
                  <div className="flex shrink-0 gap-1">
                    <Button
                      size="sm"
                      disabled={patchUser.isPending}
                      onClick={() => approve(u)}
                    >
                      <Check className="size-3.5" aria-hidden /> Duyệt
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={patchUser.isPending}
                      onClick={() => reject(u)}
                    >
                      <X className="size-3.5" aria-hidden /> Từ chối
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card title="Cảnh báo">
          {data.teamsWithoutLead.length === 0 ? (
            <p className="text-sm text-muted">Mọi tổ đều có tổ trưởng.</p>
          ) : (
            <ul className="space-y-1.5">
              {data.teamsWithoutLead.map((name) => (
                <li key={name} className="flex items-center gap-2 text-sm">
                  <Badge className="bg-warn/10 text-warn">
                    <AlertTriangle className="size-3" aria-hidden />
                  </Badge>
                  <span>
                    Tổ <b>{name}</b> chưa có tổ trưởng
                  </span>
                </li>
              ))}
            </ul>
          )}
          {data.openReports > 0 ? (
            <p className="mt-2 text-sm">
              <Link to="/admin/bao-cao" className="text-violet hover:underline">
                {data.openReports} báo cáo của người xem đang mở
              </Link>
            </p>
          ) : null}
        </Card>

        <Card title="Nội dung & lượt làm bài">
          <dl className="space-y-1.5 text-sm">
            <Row
              label="Tài liệu mới (7 ngày / 30 ngày)"
              value={`${data.contentNew.documents7d} / ${data.contentNew.documents30d}`}
            />
            <Row
              label="Bài tập mới (7 ngày / 30 ngày)"
              value={`${data.contentNew.quizzes7d} / ${data.contentNew.quizzes30d}`}
            />
            <Row
              label="Lượt làm bài (7 ngày)"
              value={data.attempts.last7d.toLocaleString("vi-VN")}
            />
            <Row
              label="Lượt đang làm dở"
              value={data.attempts.inProgress.toLocaleString("vi-VN")}
            />
          </dl>
        </Card>

        <Card
          title="Kho file"
          action={
            <Link
              to="/admin/he-thong"
              className="text-sm text-violet hover:underline"
            >
              Hệ thống
            </Link>
          }
        >
          <p className="mb-2 text-sm text-muted">
            Đã dùng{" "}
            <b className="text-ink">{formatBytes(data.storage.totalBytes)}</b> (
            {data.storage.fileCount.toLocaleString("vi-VN")} file)
          </p>
          {data.failedFiles.length === 0 ? (
            <p className="text-sm text-muted">
              Không có file nào lỗi khi xử lý.
            </p>
          ) : (
            <ul className="space-y-1.5">
              {data.failedFiles.map((f) => (
                <li
                  key={f.id}
                  className="flex items-center justify-between gap-2 text-xs"
                >
                  <span
                    className="flex min-w-0 items-center gap-1.5"
                    title={f.processingError ?? undefined}
                  >
                    <FileWarning
                      className="size-3.5 shrink-0 text-wrong"
                      aria-hidden
                    />
                    <span className="truncate">{f.originalName}</span>
                  </span>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={retryFile.isPending}
                    onClick={() => void retryFile.mutate(f.id)}
                  >
                    Chạy lại
                  </Button>
                </li>
              ))}
            </ul>
          )}
          {data.failedFiles.length > 0 ? (
            <p className="mt-2 text-xs text-muted">
              Cập nhật: {formatDateTime(data.failedFiles[0].createdAt)}
            </p>
          ) : null}
        </Card>
      </div>
    </div>
  );
}

function StatCard({
  label,
  value,
  to,
}: {
  label: string;
  value: number;
  to?: string;
}) {
  const inner = (
    <div className="rounded-card border border-grid bg-white p-4">
      <div className="text-2xl font-semibold tabular-nums">
        {value.toLocaleString("vi-VN")}
      </div>
      <div className="text-sm text-muted">{label}</div>
    </div>
  );
  return to ? <Link to={to}>{inner}</Link> : inner;
}

function Card({
  title,
  action,
  children,
}: {
  title: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="rounded-card border border-grid bg-white p-4">
      <div className="mb-3 flex items-center justify-between gap-2">
        <h2 className="text-sm font-semibold">{title}</h2>
        {action}
      </div>
      {children}
    </section>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt className="text-muted">{label}</dt>
      <dd className="font-medium tabular-nums">{value}</dd>
    </div>
  );
}

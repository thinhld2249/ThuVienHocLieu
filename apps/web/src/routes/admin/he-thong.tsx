import { Server } from "lucide-react";
import { useAdminRetryFile, useAdminSystem } from "@/features/admin/api";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";

/** /admin/he-thong — health DB/Cloudinary/Gotenberg, hàng đợi file, chạy lại job lỗi (spec §5.4). */
export function AdminSystemPage() {
  const { data, isPending } = useAdminSystem();
  const retryFile = useAdminRetryFile();

  if (isPending || !data) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} className="h-24" />
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <h1 className="text-xl font-semibold">Hệ thống</h1>

      <section className="rounded-card border border-grid bg-white p-4">
        <div className="mb-3 flex items-center gap-2">
          <Server className="size-4 text-violet" aria-hidden />
          <h2 className="text-sm font-semibold">Trạng thái dịch vụ</h2>
          <span className="ml-auto text-xs text-muted">
            Phiên bản {data.version} · UTC {formatDateTime(data.utcNow)}
          </span>
        </div>
        <div className="grid gap-2 sm:grid-cols-3">
          <HealthCard label="PostgreSQL" value={data.health.db} />
          <HealthCard label="Cloudinary" value={data.health.cloudinary} />
          <HealthCard label="Gotenberg" value={data.health.gotenberg} />
        </div>
      </section>

      <section className="rounded-card border border-grid bg-white p-4">
        <h2 className="mb-3 text-sm font-semibold">Xử lý file</h2>
        <div className="grid gap-3 sm:grid-cols-5">
          <FileCount label="Đang chờ" value={data.files.pending} />
          <FileCount label="Đang xử lý" value={data.files.processing} />
          <FileCount label="Sẵn sàng" value={data.files.ready} />
          <FileCount label="Lỗi" value={data.files.failed} />
          <FileCount label="Không áp dụng" value={data.files.notApplicable} />
        </div>

        {data.failedFiles.length > 0 ? (
          <div className="mt-4">
            <h3 className="mb-2 text-xs font-medium text-muted">
              File lỗi (chạy lại để chuyển Office → PDF)
            </h3>
            <ul className="space-y-1.5">
              {data.failedFiles.map((f) => (
                <li
                  key={f.id}
                  className="flex items-center justify-between gap-2 rounded-btn border border-grid/60 p-2 text-sm"
                >
                  <div className="min-w-0">
                    <div className="truncate font-medium">{f.originalName}</div>
                    <div className="truncate text-xs text-wrong">
                      {f.processingError ?? "Lỗi không xác định"} ·{" "}
                      {f.processingAttempts} lần thử · {formatDateTime(f.createdAt)}
                    </div>
                  </div>
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
          </div>
        ) : (
          <p className="mt-3 text-sm text-muted">
            Không có file nào đang lỗi.
          </p>
        )}
      </section>
    </div>
  );
}

function HealthCard({ label, value }: { label: string; value: string }) {
  const healthy = value === "Healthy";
  return (
    <div className="rounded-card border border-grid bg-paper/50 p-3">
      <div className="mb-1 text-xs text-muted">{label}</div>
      <Badge className={healthy ? "bg-correct/10 text-correct" : "bg-wrong/10 text-wrong"}>
        {healthy ? "Hoạt động" : "Có vấn đề"}
      </Badge>
      {!healthy ? (
        <div className="mt-1 text-xs text-wrong">{value}</div>
      ) : null}
    </div>
  );
}

function FileCount({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-card border border-grid bg-paper/50 p-3 text-center">
      <div className="text-xl font-semibold tabular-nums">
        {value.toLocaleString("vi-VN")}
      </div>
      <div className="text-xs text-muted">{label}</div>
    </div>
  );
}

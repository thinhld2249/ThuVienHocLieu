import { useEffect, useMemo, useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { Check, Copy, Pencil, Plus, QrCode, Trash2, X } from "lucide-react";
import {
  useAssignableQuizzes,
  useClassAssignments,
  useCreateAssignment,
  useDeleteAssignment,
  useUpdateAssignment,
} from "@/features/classes/api";
import type { AssignmentDto } from "@/features/classes/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/common/EmptyState";
import {
  formatDateTime,
  formatScore10,
  localInputToUtcIso,
  toLocalInput,
} from "@/lib/date";
import { cn } from "@/lib/utils";

/**
 * Bảng "Bài đã giao" cho 1 lớp (spec §7) + form tạo.
 * Dùng chung cho tab "Bài đã giao" của trang lớp và tab "Giao cho lớp" của form bài tập
 * (khi quizId khác null → chỉ giao/hiện bài tập đó).
 */
export function ClassAssignmentsPanel({
  classId,
  quizId,
}: {
  classId: number;
  quizId?: number | null;
}) {
  const { data: all, isPending } = useClassAssignments(classId);
  const create = useCreateAssignment(classId);
  const del = useDeleteAssignment(classId);
  const { quizzes: assignable } = useAssignableQuizzes();

  const [pickerQuizId, setPickerQuizId] = useState<number>(quizId ?? 0);
  useEffect(() => {
    if (quizId != null) setPickerQuizId(quizId);
  }, [quizId]);

  const [useRoster, setUseRoster] = useState(true);
  const [openAt, setOpenAt] = useState("");
  const [closeAt, setCloseAt] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [created, setCreated] = useState<AssignmentDto | null>(null);
  const [qrFor, setQrFor] = useState<AssignmentDto | null>(null);
  const [editFor, setEditFor] = useState<AssignmentDto | null>(null);

  const rows = useMemo(
    () =>
      (all ?? []).filter((a) => (quizId != null ? a.quizId === quizId : true)),
    [all, quizId],
  );

  const doCreate = async () => {
    setFormError(null);
    const qid = quizId ?? pickerQuizId;
    if (qid === 0) {
      setFormError("Chọn bài tập cần giao.");
      return;
    }
    const open = localInputToUtcIso(openAt);
    const close = localInputToUtcIso(closeAt);
    try {
      const a = await create.mutateAsync({
        quizId: qid,
        useRoster,
        openAt: open,
        closeAt: close,
      });
      setOpenAt("");
      setCloseAt("");
      setCreated(a);
    } catch (e) {
      setFormError(e instanceof Error ? e.message : "Không giao được bài");
    }
  };

  return (
    <div className="space-y-4">
      {/* Form tạo */}
      <div className="rounded-card border border-grid bg-white p-4">
        <h2 className="mb-3 text-sm font-semibold">Giao bài mới</h2>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {quizId == null ? (
            <label className="block">
              <span className="mb-1 block text-xs font-medium text-muted">
                Bài tập *
              </span>
              <select
                value={pickerQuizId}
                onChange={(e) => setPickerQuizId(Number(e.target.value))}
                className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
              >
                <option value={0}>Chọn bài tập…</option>
                {assignable.map((q) => (
                  <option key={q.id} value={q.id}>
                    {q.title}
                    {q.owner === "public" ? " · công khai" : ""}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Mở lúc (giờ VN, trống = ngay)
            </span>
            <input
              type="datetime-local"
              value={openAt}
              onChange={(e) => setOpenAt(e.target.value)}
              className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Đóng lúc (trống = không giới hạn)
            </span>
            <input
              type="datetime-local"
              value={closeAt}
              onChange={(e) => setCloseAt(e.target.value)}
              className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            />
          </label>
          <label className="flex items-end gap-2 pb-2 text-sm">
            <input
              type="checkbox"
              checked={useRoster}
              onChange={(e) => setUseRoster(e.target.checked)}
              className="size-4 accent-violet"
            />
            Học sinh chọn tên trong danh sách lớp
          </label>
        </div>
        {formError ? (
          <p className="mt-2 text-sm text-redpen" role="alert">
            {formError}
          </p>
        ) : null}
        <div className="mt-3 flex items-center gap-2">
          <Button onClick={() => void doCreate()} disabled={create.isPending}>
            <Plus className="size-4" aria-hidden />
            {create.isPending ? "Đang giao…" : "Giao bài"}
          </Button>
          <span className="text-xs text-muted">
            Sau khi giao, gửi mã/QR cho phụ huynh qua Zalo.
          </span>
        </div>
      </div>

      {/* Danh sách */}
      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 3 }, (_, i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : rows.length === 0 ? (
        <EmptyState
          title="Chưa có bài nào được giao"
          description="Giao bài ở form phía trên — hệ thống cấp mã 6 ký tự kèm QR để gửi nhóm Zalo."
        />
      ) : (
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[860px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="p-2">Bài tập</th>
                <th className="p-2">Mã</th>
                <th className="p-2">Mở</th>
                <th className="p-2">Đóng</th>
                <th className="p-2">Danh sách</th>
                <th className="p-2 text-right">Lượt làm</th>
                <th className="p-2 text-right">Điểm cao nhất</th>
                <th className="p-2 text-right">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((a) => (
                <tr
                  key={a.id}
                  className={cn(
                    "border-b border-grid/60 last:border-0 hover:bg-paper/50",
                    created?.id === a.id && "bg-violet/5",
                  )}
                >
                  <td className="max-w-64 p-2">
                    <div className="line-clamp-2 font-medium">
                      {a.quizTitle}
                    </div>
                    <div className="text-xs text-muted">
                      {a.creatorName} · {formatDateTime(a.createdAt)}
                    </div>
                  </td>
                  <td className="p-2">
                    <span className="rounded-btn bg-paper px-2 py-0.5 font-mono text-sm tracking-widest">
                      {a.code}
                    </span>
                  </td>
                  <td className="p-2 text-muted">
                    {a.openAt ? formatDateTime(a.openAt) : "Ngay"}
                  </td>
                  <td className="p-2 text-muted">
                    {a.closeAt ? formatDateTime(a.closeAt) : "—"}
                  </td>
                  <td className="p-2 text-muted">
                    {a.useRoster ? "Chọn tên" : "Nhập tên"}
                  </td>
                  <td className="p-2 text-right tabular-nums">
                    {a.attemptCount.toLocaleString("vi-VN")}
                  </td>
                  <td className="p-2 text-right tabular-nums">
                    {formatScore10(a.bestScore10)}
                  </td>
                  <td className="p-2">
                    <div className="flex justify-end gap-1">
                      <Button
                        size="sm"
                        variant="ghost"
                        title="QR + link"
                        aria-label={`QR bài ${a.code}`}
                        onClick={() => setQrFor(a)}
                      >
                        <QrCode className="size-3.5" aria-hidden />
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        title="Sửa"
                        aria-label={`Sửa bài ${a.code}`}
                        onClick={() => setEditFor(a)}
                      >
                        <Pencil className="size-3.5" aria-hidden />
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        className="text-redpen hover:text-redpen"
                        title="Xóa"
                        aria-label={`Xóa bài ${a.code}`}
                        disabled={del.isPending}
                        onClick={() => {
                          if (
                            confirm(
                              `Xóa bài giao "${a.quizTitle}" (mã ${a.code})?`,
                            )
                          )
                            void del.mutateAsync(a.id);
                        }}
                      >
                        <Trash2 className="size-3.5" aria-hidden />
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {qrFor ? (
        <AssignmentQrModal assignment={qrFor} onClose={() => setQrFor(null)} />
      ) : null}
      {editFor ? (
        <AssignmentEditModal
          classId={classId}
          assignment={editFor}
          onClose={() => setEditFor(null)}
        />
      ) : null}
    </div>
  );
}

/** QR + link mã giao bài để gửi nhóm Zalo phụ huynh (spec §7). */
export function AssignmentQrModal({
  assignment,
  onClose,
}: {
  assignment: AssignmentDto;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState<"link" | "code" | null>(null);
  const link = `${window.location.origin}/vao-lop?ma=${assignment.code}`;

  const copy = async (what: "link" | "code", text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(what);
      setTimeout(() => setCopied(null), 1500);
    } catch {
      // Clipboard có thể bị chặn trong webview — bỏ qua, vẫn có ô để chọn tay.
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-ink/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label="QR mã giao bài"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="w-full max-w-sm rounded-card border border-grid bg-white p-4 text-center">
        <div className="mb-2 flex items-center justify-between">
          <h3 className="font-semibold">Mã giao bài</h3>
          <Button variant="ghost" size="sm" aria-label="Đóng" onClick={onClose}>
            <X className="size-4" aria-hidden />
          </Button>
        </div>
        <p className="mb-1 truncate text-sm text-muted">
          {assignment.quizTitle}
        </p>
        <Badge className="mb-3 bg-violet/10 text-violet">
          {assignment.closeAt
            ? `Đóng lúc ${formatDateTime(assignment.closeAt)}`
            : "Không giới hạn giờ đóng"}
        </Badge>
        <div className="mx-auto mb-3 w-fit rounded-card border border-grid bg-white p-2">
          <QRCodeSVG value={link} size={180} />
        </div>
        <p className="mb-1 font-mono text-lg tracking-[0.3em]">
          {assignment.code}
        </p>
        <p className="mb-3 break-all text-xs text-muted">{link}</p>
        <div className="flex justify-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => void copy("link", link)}
          >
            {copied === "link" ? (
              <Check className="size-3.5" aria-hidden />
            ) : (
              <Copy className="size-3.5" aria-hidden />
            )}
            Sao chép link
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void copy("code", assignment.code)}
          >
            {copied === "code" ? (
              <Check className="size-3.5" aria-hidden />
            ) : (
              <Copy className="size-3.5" aria-hidden />
            )}
            Sao chép mã
          </Button>
        </div>
        <p className="mt-3 text-xs text-muted">
          Dán link hoặc mã vào nhóm Zalo phụ huynh — học sinh mở /vao-lop để làm
          bài.
        </p>
      </div>
    </div>
  );
}

function AssignmentEditModal({
  classId,
  assignment,
  onClose,
}: {
  classId: number;
  assignment: AssignmentDto;
  onClose: () => void;
}) {
  const update = useUpdateAssignment();
  const [useRoster, setUseRoster] = useState(assignment.useRoster);
  const [openAt, setOpenAt] = useState(toLocalInput(assignment.openAt));
  const [closeAt, setCloseAt] = useState(toLocalInput(assignment.closeAt));
  const [error, setError] = useState<string | null>(null);

  const save = async () => {
    setError(null);
    try {
      await update.mutateAsync({
        classId,
        assignmentId: assignment.id,
        body: {
          useRoster,
          openAt: localInputToUtcIso(openAt),
          closeAt: localInputToUtcIso(closeAt),
        },
      });
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không cập nhật được");
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-ink/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label="Sửa bài giao"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="w-full max-w-sm space-y-3 rounded-card border border-grid bg-white p-4">
        <h3 className="font-semibold">
          Sửa bài giao{" "}
          <span className="font-mono tracking-widest">{assignment.code}</span>
        </h3>
        <p className="truncate text-sm text-muted">{assignment.quizTitle}</p>
        <label className="block">
          <span className="mb-1 block text-xs font-medium text-muted">
            Mở lúc (giờ VN; trống = giữ nguyên)
          </span>
          <input
            type="datetime-local"
            value={openAt}
            onChange={(e) => setOpenAt(e.target.value)}
            className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          />
        </label>
        <label className="block">
          <span className="mb-1 block text-xs font-medium text-muted">
            Đóng lúc (trống = giữ nguyên)
          </span>
          <input
            type="datetime-local"
            value={closeAt}
            onChange={(e) => setCloseAt(e.target.value)}
            className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          />
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={useRoster}
            onChange={(e) => setUseRoster(e.target.checked)}
            className="size-4 accent-violet"
          />
          Học sinh chọn tên trong danh sách lớp
        </label>
        {error ? (
          <p className="text-sm text-redpen" role="alert">
            {error}
          </p>
        ) : null}
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={onClose}>
            Hủy
          </Button>
          <Button onClick={() => void save()} disabled={update.isPending}>
            {update.isPending ? "Đang lưu…" : "Lưu"}
          </Button>
        </div>
      </div>
    </div>
  );
}

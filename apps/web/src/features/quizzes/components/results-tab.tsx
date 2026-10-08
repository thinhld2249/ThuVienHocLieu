import { useState } from "react";
import { Download, Trash2 } from "lucide-react";
import { useDeleteAttempt, useQuizAttempts } from "@/features/quizzes/api";
import { formatDateTime, formatScore10 } from "@/lib/date";
import { Pagination } from "@/components/common/Pagination";
import { EmptyState } from "@/components/common/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";

/** Tab Kết quả (spec §6.8): bảng lượt làm + lọc "lượt" + xóa + xuất Excel. */
export function ResultsTab({ quizId }: { quizId: number }) {
  const [which, setWhich] = useState<"" | "best" | "first" | "last">("");
  const [page, setPage] = useState(1);
  const { data, isPending } = useQuizAttempts(quizId, {
    which: which === "" ? null : which,
    page,
    pageSize: 20,
  });
  const deleteAttempt = useDeleteAttempt(quizId);

  const remove = (attemptId: string, label: string) => {
    if (!confirm(`Xóa lượt làm của ${label}? Hành động không thể hoàn tác.`))
      return;
    void deleteAttempt.mutateAsync(attemptId);
  };

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <label className="flex items-center gap-2 text-sm text-muted">
          Hiển thị
          <select
            value={which}
            onChange={(e) => {
              setWhich(e.target.value as typeof which);
              setPage(1);
            }}
            className="h-9 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value="">Tất cả lượt</option>
            <option value="best">Lượt cao nhất</option>
            <option value="first">Lượt đầu</option>
            <option value="last">Lượt cuối</option>
          </select>
        </label>
        <a
          href={`/api/teacher/quizzes/${quizId}/export.xlsx`}
          className="ml-auto inline-flex items-center gap-1.5 rounded-btn border border-grid bg-white px-3 py-2 text-sm font-medium hover:border-violet"
        >
          <Download className="size-4" aria-hidden /> Xuất Excel
        </a>
      </div>

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-10 w-full" />
          ))}
        </div>
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          title="Chưa có lượt làm"
          description="Kết quả sẽ hiện ở đây sau khi học sinh nộp bài."
        />
      ) : (
        <div className="overflow-x-auto rounded-card border border-grid">
          <table className="w-full min-w-[640px] text-sm">
            <thead>
              <tr className="border-b border-grid bg-paper text-left text-muted">
                <th className="px-3 py-2 font-medium">Họ tên</th>
                <th className="px-3 py-2 font-medium">Lớp</th>
                <th className="px-3 py-2 text-right font-medium">Điểm</th>
                <th className="px-3 py-2 text-right font-medium">Câu đúng</th>
                <th className="px-3 py-2 text-right font-medium">Thời gian</th>
                <th className="px-3 py-2 font-medium">Nộp lúc</th>
                <th className="px-3 py-2 text-center font-medium">Lượt</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {data.items.map((a) => {
                const name = a.studentName ?? a.guestName ?? "—";
                return (
                  <tr key={a.id} className="border-b border-grid last:border-0">
                    <td className="px-3 py-2">{name}</td>
                    <td className="px-3 py-2 text-muted">
                      {a.studentClass ?? "—"}
                    </td>
                    <td className="px-3 py-2 text-right font-semibold tabular-nums">
                      {formatScore10(a.score10)}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-muted">
                      {a.correctCount != null && a.questionCount != null
                        ? `${a.correctCount}/${a.questionCount}`
                        : "—"}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-muted">
                      {a.durationSec != null
                        ? `${Math.max(0, Math.floor(a.durationSec / 60))}p ${
                            a.durationSec % 60
                          }s`
                        : "—"}
                    </td>
                    <td className="px-3 py-2 tabular-nums text-muted">
                      {formatDateTime(a.submittedAt)}
                    </td>
                    <td className="px-3 py-2 text-center tabular-nums text-muted">
                      {a.attemptNo}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <button
                        type="button"
                        aria-label={`Xóa lượt làm của ${name}`}
                        onClick={() => remove(a.id, name)}
                        className="rounded p-1 text-muted hover:text-redpen"
                      >
                        <Trash2 className="size-4" aria-hidden />
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {data ? (
        <Pagination
          page={page}
          pageSize={20}
          total={data.total}
          onChange={setPage}
        />
      ) : null}
    </div>
  );
}

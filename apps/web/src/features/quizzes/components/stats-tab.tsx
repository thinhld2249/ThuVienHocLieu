import { useQuizStats } from "@/features/quizzes/api";
import { formatScore10 } from "@/lib/date";
import { EmptyState } from "@/components/common/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

/** Tab Thống kê (spec §6.8): điểm TB, phân bố, % đúng từng câu. */
export function StatsTab({ quizId }: { quizId: number }) {
  const { data, isPending } = useQuizStats(quizId);

  if (isPending)
    return (
      <div className="space-y-3">
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );

  if (!data || data.attemptCount === 0)
    return (
      <EmptyState
        title="Chưa có dữ liệu thống kê"
        description="Thống kê sẽ hiện sau khi có lượt làm bài."
      />
    );

  const maxBucket = Math.max(1, ...data.distribution.map((b) => b.count));

  return (
    <div className="space-y-6">
      {/* Chỉ số tổng quan */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
        <StatCard label="Lượt làm" value={String(data.attemptCount)} />
        <StatCard
          label="Điểm TB"
          value={formatScore10(data.averageScore10)}
        />
        <StatCard
          label="Trung vị"
          value={formatScore10(data.medianScore10)}
        />
        <StatCard
          label="Thấp nhất"
          value={formatScore10(data.minScore10)}
        />
        <StatCard
          label="Cao nhất"
          value={formatScore10(data.maxScore10)}
        />
      </div>

      {/* Phân bố điểm */}
      <section>
        <h3 className="mb-3 text-sm font-semibold text-muted">
          Phân bố điểm
        </h3>
        <div className="flex h-36 items-end gap-1.5 rounded-card border border-grid bg-white p-3">
          {data.distribution.map((b) => (
            <div
              key={b.score10}
              className="flex h-full flex-1 flex-col items-center justify-end gap-1"
              title={`${b.score10} điểm: ${b.count} lượt`}
            >
              <span className="text-[10px] tabular-nums text-muted">
                {b.count > 0 ? b.count : ""}
              </span>
              <div
                className="w-full rounded-t-sm bg-violet/70"
                style={{
                  height: `${Math.round((b.count / maxBucket) * 100)}%`,
                }}
              />
              <span className="text-[10px] tabular-nums text-muted">
                {b.score10}
              </span>
            </div>
          ))}
        </div>
      </section>

      {/* Thống kê từng câu */}
      <section>
        <h3 className="mb-3 text-sm font-semibold text-muted">
          Theo câu hỏi
        </h3>
        <p className="mb-2 text-xs text-muted">
          Câu có tỷ lệ đúng dưới 30% được tô vàng — có thể đề hoặc đáp án cần
          kiểm tra lại.
        </p>
        <div className="space-y-2">
          {data.questions.map((q) => (
            <QuestionStat key={q.id} q={q} />
          ))}
        </div>
      </section>
    </div>
  );
}

function StatCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-card border border-grid bg-white p-3">
      <p className="text-xs text-muted">{label}</p>
      <p className="mt-1 text-xl font-semibold tabular-nums">{value}</p>
    </div>
  );
}

function QuestionStat({
  q,
}: {
  q: {
    id: number;
    number: number;
    preview: string;
    attempted: number;
    correctPercent: number;
    optionPicks: {
      optionId: number;
      preview: string;
      picks: number;
      isCorrect: boolean;
    }[];
  }
}) {
  const maxPick = Math.max(1, ...q.optionPicks.map((o) => o.picks));
  const low = q.correctPercent < 30;
  return (
    <div
      className={cn(
        "rounded-card border p-3",
        low ? "border-warn/50 bg-warn/10" : "border-grid bg-white",
      )}
    >
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="text-sm font-semibold">Câu {q.number}</span>
        <span className="min-w-0 flex-1 line-clamp-1 text-sm text-muted">
          {q.preview || "(chưa có nội dung)"}
        </span>
        <span
          className={cn(
            "text-sm font-semibold tabular-nums",
            low ? "text-warn" : "text-correct",
          )}
        >
          {Math.round(q.correctPercent)}% đúng
        </span>
        <span className="text-xs tabular-nums text-muted">
          {q.attempted} lượt
        </span>
      </div>
      {q.optionPicks.length > 0 ? (
        <div className="mt-2 space-y-1">
          {q.optionPicks.map((o) => (
            <div key={o.optionId} className="flex items-center gap-2">
              <span className="w-4 text-xs font-medium">
                {String.fromCharCode(65 + q.optionPicks.indexOf(o))}
              </span>
              <span
                className={cn(
                  "line-clamp-1 w-40 shrink-0 text-xs",
                  o.isCorrect ? "font-medium text-correct" : "text-muted",
                )}
                title={o.preview}
              >
                {o.isCorrect ? "✓ " : ""}
                {o.preview || "—"}
              </span>
              <div className="h-2 flex-1 overflow-hidden rounded-full bg-grid/60">
                <div
                  className={cn(
                    "h-full rounded-full",
                    o.isCorrect ? "bg-correct/70" : "bg-violet/50",
                  )}
                  style={{ width: `${(o.picks / maxPick) * 100}%` }}
                />
              </div>
              <span className="w-6 text-right text-xs tabular-nums text-muted">
                {o.picks}
              </span>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}

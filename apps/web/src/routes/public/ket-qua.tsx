import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { Check, ChevronRight, RotateCcw, X } from "lucide-react";
import {
  listLocalAttempts,
  removeLocalAttempt,
  useAttempt,
} from "@/features/attempts/api";
import { QuizHtml } from "@/features/quizzes/components/attempt-runner";
import { contentUrl } from "@/lib/slug";
import { formatDateTime, formatScore10 } from "@/lib/date";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/common/EmptyState";
import { cn } from "@/lib/utils";

function formatDuration(sec: number | null): string {
  if (sec == null) return "—";
  const m = Math.floor(sec / 60);
  const s = sec % 60;
  return m > 0 ? `${m} phút ${s > 0 ? `${s} giây` : ""}`.trim() : `${s} giây`;
}

function optionLabel(i: number): string {
  return String.fromCharCode(65 + Math.min(i, 25));
}

/** /ket-qua/:attemptId — kết quả (spec §6.7, §14.3). */
export function KetQuaPage() {
  const { attemptId } = useParams<{ attemptId: string }>();
  const navigate = useNavigate();
  const { data, isPending, isError } = useAttempt(attemptId ?? null);

  // Slug quiz từ local ref (đọc TRƯỚC khi cleanup xóa) cho nút "Làm lại".
  const [localRef] = useState(
    () => listLocalAttempts().find((a) => a.attemptId === attemptId) ?? null,
  );

  // Attempt còn dở → về màn làm bài.
  useEffect(() => {
    if (data && data.status === "InProgress")
      navigate(`/lam-bai/${data.id}`, { replace: true });
  }, [data, navigate]);

  useEffect(() => {
    if (data && data.status !== "InProgress") removeLocalAttempt(data.id);
  }, [data]);

  if (isPending || !data)
    return (
      <div className="o-li min-h-dvh">
        <div className="mx-auto w-full max-w-2xl px-4 py-10">
          <Skeleton className="mx-auto mb-6 h-32 w-40" />
          <Skeleton className="mb-3 h-5 w-2/3" />
          <Skeleton className="h-5 w-1/2" />
        </div>
      </div>
    );

  if (isError || data.result == null)
    return (
      <div className="o-li min-h-dvh">
        <div className="mx-auto w-full max-w-2xl px-4 py-10">
          <EmptyState
            title="Không tìm thấy kết quả"
            description="Lượt làm bài không tồn tại hoặc chưa được chấm."
            action={
              <Link to="/" className="text-sm text-violet hover:underline">
                Về trang chủ
              </Link>
            }
          />
        </div>
      </div>
    );

  const result = data.result;
  const expired = data.status === "Expired";
  const canRetry =
    data.maxAttempts != null && data.usedAttempts < data.maxAttempts;

  return (
    <div className="o-li min-h-dvh">
      <div className="mx-auto w-full max-w-2xl px-4 py-10">
        {/* Con điểm bút đỏ (spec §14.3) */}
        <div className="mb-8 flex flex-col items-center">
          <span
            className="score-pop inline-block text-7xl font-bold italic leading-none text-redpen"
            style={{ transform: "rotate(-4deg)" }}
            role="img"
            aria-label={`Điểm 10: ${formatScore10(result.score10)}`}
          >
            {formatScore10(result.score10)}
          </span>
          <span className="mt-2 text-sm text-muted">điểm thang 10</span>
          {expired ? (
            <Badge className="mt-3 bg-warn/10 text-warn">
              Hết giờ — hệ thống tự nộp
            </Badge>
          ) : null}
        </div>

        {/* Số liệu */}
        <dl className="mb-8 grid grid-cols-2 gap-3 sm:grid-cols-3">
          <div className="rounded-card border border-grid bg-white p-3 text-center">
            <dt className="text-xs text-muted">Câu đúng</dt>
            <dd className="text-xl font-semibold tabular-nums">
              {result.correctCount != null && result.questionCount != null
                ? `${result.correctCount}/${result.questionCount}`
                : "—"}
            </dd>
          </div>
          <div className="rounded-card border border-grid bg-white p-3 text-center">
            <dt className="text-xs text-muted">Thời gian làm</dt>
            <dd className="text-xl font-semibold">
              {formatDuration(result.durationSec)}
            </dd>
          </div>
          <div className="col-span-2 rounded-card border border-grid bg-white p-3 text-center sm:col-span-1">
            <dt className="text-xs text-muted">Nộp lúc</dt>
            <dd className="text-base font-medium">
              {formatDateTime(result.submittedAt)}
            </dd>
          </div>
        </dl>

        {/* Xem lại đáp án */}
        {result.canReview && result.review ? (
          <section aria-label="Xem lại từng câu" className="mb-8">
            <h2 className="mb-3 text-lg font-semibold">Xem lại từng câu</h2>
            <ol className="space-y-3">
              {result.review.map((q) => {
                const allCorrect =
                  q.options.filter((o) => o.isCorrect).length > 0 &&
                  q.options.filter((o) => o.isCorrect).every((o) => o.selected);
                return (
                  <li
                    key={q.id}
                    className={cn(
                      "rounded-card border bg-white p-4",
                      allCorrect ? "border-correct/40" : "border-wrong/40",
                    )}
                  >
                    <div className="mb-2 flex items-start gap-2">
                      <span
                        className={cn(
                          "mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full text-white",
                          allCorrect ? "bg-correct" : "bg-wrong",
                        )}
                        aria-hidden
                      >
                        {allCorrect ? (
                          <Check className="size-3.5" />
                        ) : (
                          <X className="size-3.5" />
                        )}
                      </span>
                      <div className="min-w-0 text-[15px]">
                        <span className="mb-1 block text-xs text-muted">
                          Câu {q.number}
                        </span>
                        <QuizHtml html={q.contentHtml} />
                      </div>
                    </div>
                    <ul className="ml-7 space-y-1.5">
                      {q.options.map((o, i) => (
                        <li
                          key={o.id}
                          className={cn(
                            "flex items-start gap-2 rounded-btn border px-2.5 py-1.5 text-sm",
                            o.isCorrect
                              ? "border-correct/40 bg-correct/10"
                              : o.selected
                                ? "border-wrong/40 bg-wrong/10"
                                : "border-grid/60 bg-paper",
                          )}
                        >
                          <span className="shrink-0 font-semibold">
                            {optionLabel(i)}.
                          </span>
                          <span className="min-w-0 flex-1">
                            <QuizHtml html={o.contentHtml} />
                          </span>
                          {o.selected ? (
                            <Badge
                              variant={o.isCorrect ? "success" : "warning"}
                              className="shrink-0"
                            >
                              {o.isCorrect ? "Bạn chọn · đúng" : "Bạn chọn"}
                            </Badge>
                          ) : null}
                          {o.isCorrect ? (
                            <Badge variant="success" className="shrink-0">
                              Đáp án
                            </Badge>
                          ) : null}
                        </li>
                      ))}
                    </ul>
                    {q.explanationHtml ? (
                      <div className="ml-7 mt-2 rounded-btn bg-paper p-2.5 text-sm text-muted">
                        <QuizHtml html={q.explanationHtml} />
                      </div>
                    ) : null}
                  </li>
                );
              })}
            </ol>
          </section>
        ) : (
          <p className="mb-8 rounded-card border border-grid bg-white p-4 text-sm text-muted">
            Giáo viên sẽ công bố đáp án sau.
          </p>
        )}

        {/* Hành động */}
        <div className="flex flex-wrap items-center gap-2">
          {canRetry && localRef ? (
            <Button
              onClick={() =>
                navigate(
                  contentUrl("bai-tap", localRef.quizSlug, localRef.quizId),
                )
              }
            >
              <RotateCcw className="size-4" aria-hidden /> Làm lại
            </Button>
          ) : null}
          <Link to="/">
            <Button variant="outline">
              Về trang chủ <ChevronRight className="size-4" aria-hidden />
            </Button>
          </Link>
        </div>
      </div>
    </div>
  );
}

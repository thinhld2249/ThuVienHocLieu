import { useCallback, useEffect, useRef, useState } from "react";
import { ChevronDown, ChevronLeft, ChevronRight, Flag } from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  readLocalAnswers,
  useSaveAnswers,
  useSubmitAttempt,
  writeLocalAnswers,
} from "@/features/attempts/api";
import type {
  AttemptGroup,
  AttemptQuestion,
  SavedAnswer,
} from "@/features/attempts/types";

const FONT_STEPS = [14, 16, 18, 20, 24];
const DEBOUNCE_MS = 1500;

/** Nội dung HTML đã sanitize phía BE — render an toàn (allowlist §12). */
export function QuizHtml({
  html,
  className,
}: {
  html: string | null;
  className?: string;
}) {
  if (!html) return null;
  return (
    <div
      className={cn(
        "[&_p]:my-1 [&_img]:max-w-full [&_img]:rounded-btn [&_table]:w-full [&_table]:border-collapse [&_td]:border [&_td]:border-grid [&_td]:p-1.5 [&_th]:border [&_th]:border-grid [&_th]:p-1.5",
        className,
      )}
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
}

function optionLabel(i: number): string {
  return String.fromCharCode(65 + Math.min(i, 25));
}

/** mm:ss cho đồng hồ đếm ngược. */
function formatClock(totalSec: number): string {
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}

export interface AttemptRunnerData {
  questions: AttemptQuestion[];
  groups: AttemptGroup[];
  answers: SavedAnswer[];
}

/**
 * Giao diện làm bài mobile-first (spec §6.6, §14.3): 1 câu/màn hình,
 * thẻ phương án chạm cả thẻ, lưới câu, đếm ngược, A−/A+, lưu tự động 1,5s.
 *
 * `attemptId` = null → chế độ xem trước (GV "Xem như học sinh"): không
 * lưu server, không đếm ngược, không nộp.
 */
export function AttemptRunner({
  attemptId,
  title,
  data,
  expiresAt,
  onFinished,
  onGone,
}: {
  attemptId: string | null;
  title: string;
  data: AttemptRunnerData;
  expiresAt: string | null;
  /** Sau nộp thành công / hết giờ — FE điều hướng trang kết quả. */
  onFinished: (attemptId: string) => void;
  /** Server báo attempt đã chốt (409/410 khi lưu). */
  onGone?: (attemptId: string) => void;
}) {
  const live = attemptId != null;
  const [answers, setAnswers] = useState<Map<number, number[]>>(() => {
    const m = new Map<number, number[]>();
    for (const a of data.answers) m.set(a.questionId, [...a.optionIds]);
    if (live) {
      // Khôi phục câu trả lời chưa kịp lưu server (mất mạng/tải lại).
      for (const [qId, ids] of readLocalAnswers(attemptId!))
        if (!m.has(qId)) m.set(qId, ids);
    }
    return m;
  });
  const [current, setCurrent] = useState(0);
  const [marked, setMarked] = useState<Set<number>>(new Set());
  const [fontIdx, setFontIdx] = useState(() => {
    const v = Number(sessionStorage.getItem("hl_font") ?? 2);
    return v >= 0 && v < FONT_STEPS.length ? v : 2;
  });
  const [showAll, setShowAll] = useState(
    () => window.matchMedia("(min-width: 768px)").matches,
  );
  const [passageOpen, setPassageOpen] = useState<Set<number>>(new Set());
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [saveState, setSaveState] = useState<
    "idle" | "saving" | "saved" | "error"
  >("idle");
  const [timeLeft, setTimeLeft] = useState<number | null>(null);

  const questions = data.questions;

  const save = useSaveAnswers(live ? attemptId : null);
  const submit = useSubmitAttempt(live ? attemptId : null);

  const answersRef = useRef(answers);
  answersRef.current = answers;
  const dirtyRef = useRef(false);
  const timerRef = useRef<number | null>(null);
  const finishedRef = useRef(false);

  const answeredCount = answers.size;
  const unanswered = questions.length - answeredCount;

  // ===== Lưu tự động: debounce 1,5s (spec §6.6) =====
  const flush = useCallback(async () => {
    if (!live || attemptId == null) return;
    if (timerRef.current != null) {
      window.clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    if (!dirtyRef.current) return;
    dirtyRef.current = false;
    setSaveState("saving");
    const items: SavedAnswer[] = [...answersRef.current.entries()]
      .filter(([, ids]) => ids.length > 0)
      .map(([questionId, optionIds]) => ({ questionId, optionIds }));
    try {
      await save.mutateAsync(items);
      setSaveState("saved");
    } catch (e) {
      const status = (e as { data?: { status?: number } })?.data?.status;
      if (status === 410 || status === 409) {
        if (!finishedRef.current) {
          finishedRef.current = true;
          onGone?.(attemptId);
        }
        return;
      }
      setSaveState("error");
    }
  }, [live, attemptId, save, onGone]);

  const scheduleSave = useCallback(() => {
    if (!live) return;
    dirtyRef.current = true;
    setSaveState("saving");
    if (timerRef.current != null) window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => void flush(), DEBOUNCE_MS);
  }, [live, flush]);

  // localStorage mọi lần đổi (khôi phục khi tải lại/mất mạng).
  const commitAnswer = useCallback(
    (questionId: number, optionIds: number[]) => {
      setAnswers((prev) => {
        const next = new Map(prev);
        if (optionIds.length === 0) next.delete(questionId);
        else next.set(questionId, optionIds);
        if (live && attemptId != null) writeLocalAnswers(attemptId, next);
        return next;
      });
      scheduleSave();
    },
    [live, attemptId, scheduleSave],
  );

  const toggleOption = (q: AttemptQuestion, optionId: number) => {
    const sel = answers.get(q.id) ?? [];
    if (q.type === "Multi") {
      commitAnswer(
        q.id,
        sel.includes(optionId)
          ? sel.filter((x) => x !== optionId)
          : [...sel, optionId],
      );
    } else {
      commitAnswer(q.id, [optionId]);
    }
  };

  // ===== Đồng hồ đếm ngược theo expiresAt của server =====
  useEffect(() => {
    if (!live || !expiresAt) {
      setTimeLeft(null);
      return;
    }
    const tick = () => {
      const left = Math.max(
        0,
        Math.round((new Date(expiresAt).getTime() - Date.now()) / 1000),
      );
      setTimeLeft(left);
      if (left <= 0) void doSubmit();
    };
    tick();
    const t = window.setInterval(tick, 1000);
    return () => window.clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [live, expiresAt]);

  // Dọn timer khi unmount.
  useEffect(
    () => () => {
      if (timerRef.current != null) window.clearTimeout(timerRef.current);
    },
    [],
  );

  const doSubmit = useCallback(async () => {
    if (!live || attemptId == null || finishedRef.current) return;
    finishedRef.current = true;
    try {
      await flush();
      await submit.mutateAsync();
      onFinished(attemptId);
    } catch {
      finishedRef.current = false;
    }
  }, [live, attemptId, flush, submit, onFinished]);

  // ===== Render 1 câu =====
  const renderQuestion = (q: AttemptQuestion, idx: number) => {
    const sel = answers.get(q.id) ?? [];
    const isCurrent = idx === current;
    const group =
      q.groupId != null
        ? data.groups.find((g) => g.id === q.groupId)
        : undefined;

    return (
      <div key={q.id} className={cn(!showAll && !isCurrent && "hidden")}>
        {group?.passageHtml ? (
          <details
            open={passageOpen.has(group.id)}
            onToggle={(e) => {
              const open = (e.target as HTMLDetailsElement).open;
              setPassageOpen((prev) => {
                const next = new Set(prev);
                if (open) next.add(group.id);
                else next.delete(group.id);
                return next;
              });
            }}
            className="mb-3 rounded-card border border-grid bg-white/70"
          >
            <summary className="flex cursor-pointer items-center justify-between gap-2 p-3 text-sm font-medium text-muted">
              <span>{group.title ?? "Đọc kỹ phần sau"}</span>
              <ChevronDown className="size-4 shrink-0" aria-hidden />
            </summary>
            <div className="border-t border-grid p-3 text-[0.95em] leading-relaxed">
              <QuizHtml html={group.passageHtml} />
            </div>
          </details>
        ) : null}

        <div className="rounded-card border border-grid bg-white p-4">
          <div className="mb-3 flex items-start justify-between gap-2">
            <div className="min-w-0">
              <span className="mb-1 block text-xs font-medium text-muted">
                Câu {q.number}/{questions.length}
                {q.type === "Multi" ? " · chọn nhiều" : ""}
              </span>
              <QuizHtml html={q.contentHtml} />
            </div>
            <button
              type="button"
              aria-pressed={marked.has(q.id)}
              aria-label={`Đánh dấu xem lại câu ${q.number}`}
              title="Đánh dấu xem lại"
              onClick={() =>
                setMarked((prev) => {
                  const next = new Set(prev);
                  if (next.has(q.id)) next.delete(q.id);
                  else next.add(q.id);
                  return next;
                })
              }
              className={cn(
                "shrink-0 rounded-btn p-1.5 transition",
                marked.has(q.id)
                  ? "bg-warn/15 text-warn"
                  : "text-muted/50 hover:text-warn",
              )}
            >
              <Flag className="size-4" aria-hidden />
            </button>
          </div>

          <div
            role={q.type === "Multi" ? "group" : "radiogroup"}
            aria-label={`Các phương án câu ${q.number}`}
            className="space-y-2"
          >
            {q.options.map((o, i) => {
              const selected = sel.includes(o.id);
              return (
                <button
                  key={o.id}
                  type="button"
                  role={q.type === "Multi" ? "checkbox" : "radio"}
                  aria-checked={selected}
                  aria-label={`Phương án ${optionLabel(i)}`}
                  onClick={() => toggleOption(q, o.id)}
                  className={cn(
                    "flex w-full min-h-14 items-center gap-3 rounded-option border bg-white p-3 text-left transition",
                    selected
                      ? "border-violet bg-violet/10 ring-1 ring-violet"
                      : "border-grid hover:border-violet/40",
                  )}
                >
                  <span
                    className={cn(
                      "flex size-8 shrink-0 items-center justify-center rounded-full border text-sm font-semibold",
                      selected
                        ? "border-violet bg-violet text-white"
                        : "border-grid text-muted",
                      q.type === "Multi" && "rounded-md",
                    )}
                  >
                    {optionLabel(i)}
                  </span>
                  <span className="min-w-0 flex-1 text-[1em] leading-snug">
                    <QuizHtml html={o.contentHtml} />
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      </div>
    );
  };

  // ===== Lưới câu =====
  const questionGrid = (
    <div
      className="mb-3 grid grid-cols-8 gap-1.5 sm:grid-cols-10"
      role="group"
      aria-label="Danh sách câu hỏi"
    >
      {questions.map((q, i) => {
        const done = (answers.get(q.id) ?? []).length > 0;
        return (
          <button
            key={q.id}
            type="button"
            aria-label={`Câu ${q.number}${done ? " (đã làm)" : ""}`}
            aria-current={i === current ? "step" : undefined}
            onClick={() => {
              setCurrent(i);
              setShowAll(false);
            }}
            className={cn(
              "relative flex h-9 items-center justify-center rounded-btn border text-sm tabular-nums transition",
              i === current
                ? "border-violet bg-violet text-white"
                : done
                  ? "border-violet/40 bg-violet/10 text-violet"
                  : "border-grid bg-white text-muted hover:border-violet/40",
            )}
          >
            {q.number}
            {marked.has(q.id) ? (
              <span
                aria-hidden
                className="absolute -right-0.5 -top-0.5 size-2 rounded-full bg-warn"
              />
            ) : null}
          </button>
        );
      })}
    </div>
  );

  if (questions.length === 0)
    return (
      <div className="space-y-3">
        <Skeleton className="h-10" />
        <Skeleton className="h-64" />
      </div>
    );

  return (
    <div
      style={{ fontSize: FONT_STEPS[fontIdx] }}
      className="o-li min-h-dvh bg-paper pb-24"
    >
      {/* Thanh trên */}
      <header className="sticky top-0 z-20 border-b border-grid bg-paper/95 backdrop-blur">
        <div className="mx-auto flex w-full max-w-3xl items-center gap-2 px-4 py-2.5">
          <span className="min-w-0 flex-1 truncate text-sm font-semibold">
            {title}
          </span>
          <div
            className="flex items-center gap-0.5"
            role="group"
            aria-label="Cỡ chữ"
          >
            <button
              type="button"
              aria-label="Giảm cỡ chữ"
              onClick={() =>
                setFontIdx((i) => {
                  const n = Math.max(0, i - 1);
                  sessionStorage.setItem("hl_font", String(n));
                  return n;
                })
              }
              className="rounded-btn px-1.5 py-0.5 text-xs text-muted hover:bg-grid/40"
            >
              A−
            </button>
            <button
              type="button"
              aria-label="Tăng cỡ chữ"
              onClick={() =>
                setFontIdx((i) => {
                  const n = Math.min(FONT_STEPS.length - 1, i + 1);
                  sessionStorage.setItem("hl_font", String(n));
                  return n;
                })
              }
              className="rounded-btn px-1.5 py-0.5 text-xs text-muted hover:bg-grid/40"
            >
              A+
            </button>
          </div>
          {timeLeft != null && live ? (
            <span
              className={cn(
                "rounded-btn border px-2 py-0.5 text-sm font-semibold tabular-nums",
                timeLeft <= 60
                  ? "border-redpen/40 bg-redpen/10 text-redpen"
                  : "border-grid bg-white text-ink",
              )}
              role="timer"
              aria-label={`Còn ${formatClock(timeLeft)}`}
            >
              {formatClock(timeLeft)}
            </span>
          ) : null}
        </div>
        {/* Tiến trình */}
        <div
          className="h-1 w-full bg-grid/40"
          role="progressbar"
          aria-valuenow={answeredCount}
          aria-valuemin={0}
          aria-valuemax={questions.length}
          aria-label={`${answeredCount}/${questions.length} câu đã làm`}
        >
          <div
            className="h-full bg-violet transition-all"
            style={{
              width: `${(answeredCount / questions.length) * 100}%`,
            }}
          />
        </div>
      </header>

      <main className="mx-auto w-full max-w-3xl px-4 pt-4">
        {questionGrid}

        {/* Câu hiện tại (mobile: 1 câu; desktop: hiện tất cả nếu bật) */}
        <div className="space-y-4">{questions.map(renderQuestion)}</div>

        {showAll ? (
          <Button
            variant="ghost"
            size="sm"
            className="mt-4"
            onClick={() => setShowAll(false)}
          >
            Chỉ hiện câu đang xem
          </Button>
        ) : null}

        {/* Trạng thái lưu (chế độ làm bài thật) */}
        {live ? (
          <p className="mt-3 text-center text-xs text-muted" aria-live="polite">
            {saveState === "saving"
              ? "Đang lưu…"
              : saveState === "saved"
                ? "Đã lưu"
                : saveState === "error"
                  ? "Lưu chưa thành công — sẽ thử lại"
                  : ""}
          </p>
        ) : (
          <p className="mt-3 text-center text-xs text-warn">
            Chế độ xem trước — không lưu, không nộp
          </p>
        )}

        {/* Điều hướng */}
        {live ? (
          <div className="sticky bottom-0 z-20 -mx-4 mt-6 border-t border-grid bg-paper/95 px-4 py-3 backdrop-blur">
            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                disabled={current === 0}
                onClick={() => setCurrent((i) => Math.max(0, i - 1))}
              >
                <ChevronLeft className="size-4" aria-hidden /> Câu trước
              </Button>
              <div className="flex-1 text-center text-sm text-muted">
                Câu {current + 1}/{questions.length}
              </div>
              <Button
                variant="outline"
                disabled={current >= questions.length - 1}
                onClick={() =>
                  setCurrent((i) => Math.min(questions.length - 1, i + 1))
                }
              >
                Câu sau <ChevronRight className="size-4" aria-hidden />
              </Button>
            </div>
            <div className="mt-2 flex items-center gap-2">
              <Button
                variant="ghost"
                size="sm"
                className="flex-1"
                onClick={() => setShowAll((v) => !v)}
              >
                {showAll ? "1 câu/màn" : "▦ Danh sách câu"}
              </Button>
              <Button
                className="flex-1"
                disabled={submit.isPending}
                onClick={() => setConfirmOpen(true)}
              >
                Nộp bài
              </Button>
            </div>
          </div>
        ) : (
          <div className="mt-6 flex items-center gap-2">
            <Button
              variant="outline"
              disabled={current === 0}
              onClick={() => setCurrent((i) => Math.max(0, i - 1))}
            >
              <ChevronLeft className="size-4" aria-hidden /> Câu trước
            </Button>
            <Button
              variant="outline"
              disabled={current >= questions.length - 1}
              onClick={() =>
                setCurrent((i) => Math.min(questions.length - 1, i + 1))
              }
            >
              Câu sau <ChevronRight className="size-4" aria-hidden />
            </Button>
          </div>
        )}
      </main>

      {/* Xác nhận nộp */}
      {confirmOpen ? (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
          role="dialog"
          aria-modal="true"
          aria-label="Xác nhận nộp bài"
        >
          <div className="w-full max-w-sm rounded-card bg-white p-5">
            <h2 className="mb-2 font-semibold">Nộp bài?</h2>
            <p className="mb-4 text-sm text-muted">
              {unanswered > 0
                ? `Bạn còn ${unanswered} câu chưa làm. Sau khi nộp không thể sửa được.`
                : "Bạn đã làm đủ câu. Sau khi nộp không thể sửa được."}
            </p>
            <div className="flex justify-end gap-2">
              <Button
                variant="ghost"
                onClick={() => setConfirmOpen(false)}
                disabled={submit.isPending}
              >
                Làm tiếp
              </Button>
              <Button
                onClick={() => void doSubmit()}
                disabled={submit.isPending}
              >
                {submit.isPending ? "Đang nộp…" : "Nộp bài"}
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}

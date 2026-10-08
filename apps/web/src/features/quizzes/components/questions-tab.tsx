import { useEffect, useState } from "react";
import {
  AlertTriangle,
  ArrowDown,
  ArrowUp,
  Eye,
  Plus,
  Trash2,
} from "lucide-react";
import type { QuizWarning, QuestionType } from "@/features/quizzes/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

// ===== Chuyển đổi text ⇄ HTML (giữ HTML gốc khi chưa sửa — M4: textarea) =====

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

/** HTML đã sanitize → text thuần để hiển thị trong textarea. */
export function htmlToText(html: string | null | undefined): string {
  if (!html) return "";
  const doc = new DOMParser().parseFromString(html, "text/html");
  return (doc.body.textContent ?? "").replace(/\u00a0/g, " ").trim();
}

/**
 * Text từ textarea → HTML. Nếu text khớp nội dung HTML gốc (GV không sửa)
 * → trả nguyên HTML gốc (giữ định dạng/ảnh từ import).
 */
export function textToHtml(
  text: string,
  originalHtml: string | null | undefined,
): string {
  const t = text.trim();
  if (t.length === 0) return "";
  if (
    originalHtml &&
    htmlToText(originalHtml) === t &&
    htmlToText(originalHtml).length > 0
  )
    return originalHtml;
  if (t.startsWith("<")) return t; // dán sẵn HTML → BE sanitize
  return t
    .split(/\n+/)
    .filter((l) => l.trim().length > 0)
    .map((l) => `<p>${escapeHtml(l.trim())}</p>`)
    .join("");
}

// ===== Trạng thái editor (lifted từ trang form) =====

export interface EditorOption {
  id?: number;
  sort: number;
  text: string;
  isCorrect: boolean;
  htmlOrig: string;
}

export interface EditorQuestion {
  id?: number;
  sort: number;
  groupId?: number | null;
  type: QuestionType;
  contentText: string;
  contentHtmlOrig: string;
  explanationText: string;
  explanationHtmlOrig: string | null;
  points: number;
  options: EditorOption[];
  /** true = câu có trong danh sách cảnh báo import. */
  hasWarning?: boolean;
}

export interface EditorGroup {
  id?: number;
  sort: number;
  title: string;
  passageText: string;
  passageHtmlOrig: string | null;
}

const OPTION_LABELS = "ABCDEFGH";

function labelOf(sort: number): string {
  return OPTION_LABELS[sort - 1] ?? "?";
}

/** Câu/nhóm mới thêm chưa có id thật (BE trả về sau save) → id tạm âm. */
let tempIdSeq = 0;
function nextTempId(): number {
  return --tempIdSeq;
}

export function isTempId(id: number | undefined): boolean {
  return id != null && id < 0;
}

/** Tab Câu hỏi: danh sách bên trái + editor bên phải (spec §6.5). */
export function QuestionsTab({
  questions,
  setQuestions,
  groups,
  setGroups,
  warnings,
  onPreview,
}: {
  questions: EditorQuestion[];
  setQuestions: (updater: (prev: EditorQuestion[]) => EditorQuestion[]) => void;
  groups: EditorGroup[];
  setGroups: (updater: (prev: EditorGroup[]) => EditorGroup[]) => void;
  warnings: QuizWarning[];
  onPreview: () => void;
}) {
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [warnOnly, setWarnOnly] = useState(false);

  useEffect(() => {
    if (questions.length === 0) {
      setSelectedId(null);
      return;
    }
    if (selectedId == null || !questions.some((q) => q.id === selectedId))
      setSelectedId(questions[0].id ?? null);
  }, [questions, selectedId]);

  const selected = questions.find((q) => q.id === selectedId) ?? null;
  const shown = warnOnly ? questions.filter((q) => q.hasWarning) : questions;

  const patchQuestion = (
    id: number | undefined,
    patch: Partial<EditorQuestion>,
  ) => {
    setQuestions((prev) =>
      prev.map((q) => (q.id === id ? { ...q, ...patch } : q)),
    );
  };

  const moveQuestion = (index: number, dir: -1 | 1) => {
    setQuestions((prev) => {
      const next = [...prev];
      const j = index + dir;
      if (j < 0 || j >= next.length) return prev;
      [next[index], next[j]] = [next[j], next[index]];
      return next.map((q, i) => ({ ...q, sort: i + 1 }));
    });
  };

  const removeQuestion = (id: number) => {
    if (!confirm("Xóa câu này?")) return;
    setQuestions((prev) =>
      prev.filter((q) => q.id !== id).map((q, i) => ({ ...q, sort: i + 1 })),
    );
  };

  const addQuestion = () => {
    const tempId = nextTempId();
    setQuestions((prev) => [
      ...prev,
      {
        id: tempId,
        sort: prev.length + 1,
        groupId:
          groups.length > 0 ? (groups[groups.length - 1].id ?? null) : null,
        type: "Single",
        contentText: "",
        contentHtmlOrig: "",
        explanationText: "",
        explanationHtmlOrig: null,
        points: 1,
        options: [
          { sort: 1, text: "", isCorrect: false, htmlOrig: "" },
          { sort: 2, text: "", isCorrect: false, htmlOrig: "" },
          { sort: 3, text: "", isCorrect: false, htmlOrig: "" },
          { sort: 4, text: "", isCorrect: false, htmlOrig: "" },
        ],
      },
    ]);
    setSelectedId(tempId);
  };

  const effectiveSelected = selected;

  const patchGroup = (id: number | undefined, patch: Partial<EditorGroup>) => {
    setGroups((prev) =>
      prev.map((g) => (g.id === id ? { ...g, ...patch } : g)),
    );
  };

  const moveGroup = (index: number, dir: -1 | 1) => {
    setGroups((prev) => {
      const next = [...prev];
      const j = index + dir;
      if (j < 0 || j >= next.length) return prev;
      [next[index], next[j]] = [next[j], next[index]];
      return next.map((g, i) => ({ ...g, sort: i + 1 }));
    });
  };

  const removeGroup = (id: number) => {
    if (!confirm("Xóa nhóm này? Các câu trong nhóm thành 'không nhóm'."))
      return;
    setGroups((prev) =>
      prev.filter((g) => g.id !== id).map((g, i) => ({ ...g, sort: i + 1 })),
    );
    setQuestions((prev) =>
      prev.map((q) => (q.groupId === id ? { ...q, groupId: null } : q)),
    );
  };

  const addGroup = () => {
    const tempId = nextTempId();
    setGroups((prev) => [
      ...prev,
      {
        id: tempId,
        sort: prev.length + 1,
        title: `PHẦN ${prev.length + 1}`,
        passageText: "",
        passageHtmlOrig: null,
      },
    ]);
  };

  return (
    <div>
      {/* Banner cảnh báo import (spec §6.5) */}
      {warnings.length > 0 ? (
        <div className="mb-4 rounded-card border border-warn/40 bg-warn/10 p-3">
          <div className="flex flex-wrap items-center gap-2">
            <AlertTriangle className="size-4 text-warn" aria-hidden />
            <p className="text-sm font-medium text-warn">
              {warnings.length} câu cần kiểm tra trước khi mở bài tập
            </p>
            <label className="ml-auto flex cursor-pointer items-center gap-1.5 text-xs text-muted">
              <input
                type="checkbox"
                checked={warnOnly}
                onChange={(e) => setWarnOnly(e.target.checked)}
                className="accent-violet"
              />
              Chỉ hiện câu có cảnh báo
            </label>
          </div>
          <ul className="mt-2 space-y-1 text-sm">
            {warnings.map((w, i) => (
              <li key={i} className="text-ink">
                {w.questionNumber != null ? (
                  <span className="mr-1 font-medium">
                    Câu {w.questionNumber}:
                  </span>
                ) : null}
                {w.message}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(240px,1fr)_2fr]">
        {/* Danh sách câu */}
        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-semibold text-muted">
              Câu hỏi ({questions.length})
            </h3>
            <Button size="sm" variant="outline" onClick={addQuestion}>
              <Plus className="size-3.5" aria-hidden /> Thêm câu
            </Button>
          </div>
          {shown.length === 0 ? (
            <p className="rounded-card border border-dashed border-grid p-4 text-center text-sm text-muted">
              {warnOnly
                ? "Không có câu nào có cảnh báo."
                : "Chưa có câu hỏi. Thêm câu đầu tiên."}
            </p>
          ) : (
            <ul className="space-y-1.5">
              {shown.map((q) => {
                const idx = questions.indexOf(q);
                return (
                  <li key={q.id ?? `new-${idx}`}>
                    <div
                      className={cn(
                        "flex items-center gap-2 rounded-card border bg-white p-2 transition",
                        effectiveSelected?.id === q.id
                          ? "border-violet ring-1 ring-violet/30"
                          : "border-grid hover:border-violet/40",
                      )}
                    >
                      <button
                        type="button"
                        className="min-w-0 flex-1 text-left"
                        onClick={() => setSelectedId(q.id ?? null)}
                      >
                        <span className="flex items-center gap-1.5 text-sm">
                          <span className="font-medium">Câu {q.sort + 1}.</span>
                          <span className="line-clamp-1 text-muted">
                            {q.contentText || "(chưa có nội dung)"}
                          </span>
                          {q.hasWarning ? (
                            <AlertTriangle
                              className="size-3.5 shrink-0 text-warn"
                              aria-label="Cần kiểm tra"
                            />
                          ) : null}
                        </span>
                      </button>
                      <div className="flex shrink-0 gap-0.5">
                        <button
                          type="button"
                          aria-label="Đưa lên"
                          onClick={() => moveQuestion(idx, -1)}
                          className="rounded p-1 text-muted hover:text-violet"
                        >
                          <ArrowUp className="size-3.5" aria-hidden />
                        </button>
                        <button
                          type="button"
                          aria-label="Đưa xuống"
                          onClick={() => moveQuestion(idx, 1)}
                          className="rounded p-1 text-muted hover:text-violet"
                        >
                          <ArrowDown className="size-3.5" aria-hidden />
                        </button>
                        {q.id != null ? (
                          <button
                            type="button"
                            aria-label="Xóa câu"
                            onClick={() => removeQuestion(q.id!)}
                            className="rounded p-1 text-muted hover:text-redpen"
                          >
                            <Trash2 className="size-3.5" aria-hidden />
                          </button>
                        ) : null}
                      </div>
                    </div>
                  </li>
                );
              })}
            </ul>
          )}

          {/* Nhóm */}
          <div className="pt-4">
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-sm font-semibold text-muted">
                Nhóm & đoạn văn ({groups.length})
              </h3>
              <Button size="sm" variant="outline" onClick={addGroup}>
                <Plus className="size-3.5" aria-hidden /> Thêm nhóm
              </Button>
            </div>
            {groups.length === 0 ? (
              <p className="rounded-card border border-dashed border-grid p-3 text-center text-xs text-muted">
                Không có nhóm — mọi câu thuộc "không nhóm".
              </p>
            ) : (
              <ul className="space-y-2">
                {groups.map((g, i) => (
                  <li
                    key={g.id ?? `g-${i}`}
                    className="rounded-card border border-grid bg-white p-2.5"
                  >
                    <div className="mb-1.5 flex items-center gap-1.5">
                      <input
                        type="text"
                        value={g.title}
                        aria-label="Tiêu đề nhóm"
                        onChange={(e) =>
                          patchGroup(g.id, { title: e.target.value })
                        }
                        className="h-8 min-w-0 flex-1 rounded-btn border border-grid px-2 text-sm outline-none focus:border-violet"
                      />
                      <button
                        type="button"
                        aria-label="Nhóm lên trên"
                        onClick={() => moveGroup(i, -1)}
                        className="rounded p-1 text-muted hover:text-violet"
                      >
                        <ArrowUp className="size-3.5" aria-hidden />
                      </button>
                      <button
                        type="button"
                        aria-label="Nhóm xuống dưới"
                        onClick={() => moveGroup(i, 1)}
                        className="rounded p-1 text-muted hover:text-violet"
                      >
                        <ArrowDown className="size-3.5" aria-hidden />
                      </button>
                      {g.id != null ? (
                        <button
                          type="button"
                          aria-label="Xóa nhóm"
                          onClick={() => removeGroup(g.id!)}
                          className="rounded p-1 text-muted hover:text-redpen"
                        >
                          <Trash2 className="size-3.5" aria-hidden />
                        </button>
                      ) : null}
                    </div>
                    <textarea
                      value={g.passageText}
                      aria-label="Đoạn văn của nhóm"
                      rows={2}
                      placeholder="Đoạn dẫn cho nhóm (tùy chọn)…"
                      onChange={(e) =>
                        patchGroup(g.id, { passageText: e.target.value })
                      }
                      className="w-full rounded-btn border border-grid p-2 text-sm outline-none focus:border-violet"
                    />
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>

        {/* Editor câu đang chọn */}
        <div>
          {effectiveSelected ? (
            <QuestionEditor
              key={effectiveSelected.id ?? "__new__"}
              q={effectiveSelected}
              groups={groups}
              onPatch={(patch) => patchQuestion(effectiveSelected.id, patch)}
            />
          ) : (
            <p className="rounded-card border border-dashed border-grid p-6 text-center text-sm text-muted">
              Chọn một câu ở bên trái hoặc thêm câu mới.
            </p>
          )}
        </div>
      </div>

      {/* Xem như học sinh */}
      <div className="mt-6 flex justify-end">
        <Button
          variant="outline"
          onClick={onPreview}
          disabled={questions.length === 0}
        >
          <Eye className="size-4" aria-hidden /> Xem như học sinh
        </Button>
      </div>
    </div>
  );
}

function QuestionEditor({
  q,
  groups,
  onPatch,
}: {
  q: EditorQuestion;
  groups: EditorGroup[];
  onPatch: (patch: Partial<EditorQuestion>) => void;
}) {
  const multi = q.type === "Multi";

  const patchOption = (sort: number, patch: Partial<EditorOption>) => {
    onPatch({
      options: q.options.map((o) => (o.sort === sort ? { ...o, ...patch } : o)),
    });
  };

  const toggleCorrect = (sort: number) => {
    onPatch({
      options: q.options.map((o) =>
        o.sort === sort
          ? { ...o, isCorrect: multi ? !o.isCorrect : !o.isCorrect }
          : { ...o, isCorrect: multi ? o.isCorrect : false },
      ),
    });
  };

  const moveOption = (sort: number, dir: -1 | 1) => {
    const ordered = [...q.options].sort((a, b) => a.sort - b.sort);
    const i = ordered.findIndex((o) => o.sort === sort);
    const j = i + dir;
    if (i < 0 || j < 0 || j >= ordered.length) return;
    [ordered[i], ordered[j]] = [ordered[j], ordered[i]];
    onPatch({
      options: ordered.map((o, idx) => ({ ...o, sort: idx + 1 })),
    });
  };

  const removeOption = (sort: number) => {
    if (!confirm(`Xóa phương án ${labelOf(sort)}?`)) return;
    onPatch({
      options: q.options
        .filter((o) => o.sort !== sort)
        .map((o, i) => ({ ...o, sort: i + 1 })),
    });
  };

  const addOption = () => {
    if (q.options.length >= 8) return;
    onPatch({
      options: [
        ...q.options,
        {
          sort: q.options.length + 1,
          text: "",
          isCorrect: false,
          htmlOrig: "",
        },
      ],
    });
  };

  return (
    <div className="rounded-card border border-grid bg-white p-4">
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <span className="text-sm font-semibold">Câu {q.sort + 1}</span>
        <select
          aria-label="Loại câu"
          value={q.type}
          onChange={(e) => onPatch({ type: e.target.value as QuestionType })}
          className="h-9 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
        >
          <option value="Single">1 đáp án</option>
          <option value="Multi">Nhiều đáp án</option>
          <option value="TrueFalse">Đúng / Sai</option>
        </select>
        <label className="ml-auto flex items-center gap-1.5 text-sm text-muted">
          Điểm
          <input
            type="number"
            min={0}
            step={0.25}
            value={q.points}
            onChange={(e) => onPatch({ points: Number(e.target.value) || 0 })}
            className="h-9 w-20 rounded-btn border border-grid px-2 text-sm tabular-nums outline-none focus:border-violet"
          />
        </label>
        {groups.length > 0 ? (
          <select
            aria-label="Nhóm"
            value={q.groupId ?? ""}
            onChange={(e) =>
              onPatch({
                groupId: e.target.value === "" ? null : Number(e.target.value),
              })
            }
            className="h-9 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value="">Không nhóm</option>
            {groups.map((g) => (
              <option key={g.id ?? g.sort} value={g.id ?? ""}>
                {g.title || `Nhóm ${g.sort}`}
              </option>
            ))}
          </select>
        ) : null}
      </div>

      <label className="mb-1 block text-xs font-medium text-muted">
        Nội dung câu hỏi
      </label>
      <textarea
        value={q.contentText}
        rows={3}
        onChange={(e) => onPatch({ contentText: e.target.value })}
        placeholder="Nội dung câu hỏi…"
        className="mb-3 w-full rounded-btn border border-grid p-2.5 text-sm outline-none focus:border-violet"
      />

      <label className="mb-1.5 block text-xs font-medium text-muted">
        Phương án{multi ? " (nhiều đáp án đúng)" : " (chọn 1 đáp án đúng)"}
      </label>
      <div className="mb-3 space-y-2">
        {q.options.map((o) => (
          <div
            key={o.sort}
            className={cn(
              "flex items-start gap-2 rounded-card border p-2",
              o.isCorrect ? "border-correct/50 bg-correct/5" : "border-grid",
            )}
          >
            <input
              type={multi ? "checkbox" : "radio"}
              checked={o.isCorrect}
              onChange={() => toggleCorrect(o.sort)}
              aria-label={`Đáp án đúng cho phương án ${labelOf(o.sort)}`}
              className="mt-3 size-4 accent-correct"
            />
            <span className="mt-2 w-5 shrink-0 text-center text-sm font-semibold">
              {labelOf(o.sort)}
            </span>
            <textarea
              value={o.text}
              rows={1}
              onChange={(e) => patchOption(o.sort, { text: e.target.value })}
              placeholder="Nội dung phương án…"
              className="min-w-0 flex-1 resize-y rounded-btn border border-grid p-2 text-sm outline-none focus:border-violet"
            />
            <div className="flex shrink-0 flex-col gap-0.5">
              <button
                type="button"
                aria-label="Lên trên"
                onClick={() => moveOption(o.sort, -1)}
                className="rounded p-0.5 text-muted hover:text-violet"
              >
                <ArrowUp className="size-3.5" aria-hidden />
              </button>
              <button
                type="button"
                aria-label="Xuống dưới"
                onClick={() => moveOption(o.sort, 1)}
                className="rounded p-0.5 text-muted hover:text-violet"
              >
                <ArrowDown className="size-3.5" aria-hidden />
              </button>
              {q.options.length > 2 ? (
                <button
                  type="button"
                  aria-label="Xóa phương án"
                  onClick={() => removeOption(o.sort)}
                  className="rounded p-0.5 text-muted hover:text-redpen"
                >
                  <Trash2 className="size-3.5" aria-hidden />
                </button>
              ) : null}
            </div>
          </div>
        ))}
      </div>
      <Button
        size="sm"
        variant="ghost"
        onClick={addOption}
        disabled={q.options.length >= 8}
      >
        <Plus className="size-3.5" aria-hidden /> Thêm phương án
      </Button>

      <label className="mb-1 mt-4 block text-xs font-medium text-muted">
        Giải thích (tùy chọn)
      </label>
      <textarea
        value={q.explanationText}
        rows={2}
        onChange={(e) => onPatch({ explanationText: e.target.value })}
        placeholder="Lời giải / giải thích đáp án…"
        className="w-full rounded-btn border border-grid p-2 text-sm outline-none focus:border-violet"
      />

      {q.type === "Single" &&
      q.options.filter((o) => o.isCorrect).length === 0 ? (
        <Badge className="mt-2 bg-warn/10 text-warn">
          Chưa chọn đáp án đúng
        </Badge>
      ) : null}
    </div>
  );
}

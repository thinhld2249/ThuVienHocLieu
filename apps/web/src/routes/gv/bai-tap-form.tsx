import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Save, X } from "lucide-react";
import {
  useMyQuiz,
  usePublishQuiz,
  useUpdateQuiz,
} from "@/features/quizzes/api";
import type {
  IdentityMode,
  MultiScoring,
  QuizDetail,
  QuizSettings,
  QuizWarning,
  ScoreRounding,
  Scope,
  ShowAnswers,
  UpdateQuizRequest,
} from "@/features/quizzes/types";
import {
  htmlToText,
  isTempId,
  QuestionsTab,
  textToHtml,
  type EditorGroup,
  type EditorQuestion,
} from "@/features/quizzes/components/questions-tab";
import { ResultsTab } from "@/features/quizzes/components/results-tab";
import { StatsTab } from "@/features/quizzes/components/stats-tab";
import {
  AttemptRunner,
  type AttemptRunnerData,
} from "@/features/quizzes/components/attempt-runner";
import { useMyClasses } from "@/features/classes/api";
import { ClassAssignmentsPanel } from "@/features/classes/components/assignment-panel";
import {
  publishStateLabel,
  PublishControl,
} from "@/components/common/PublishControl";
import { FileDropzone } from "@/components/common/FileDropzone";
import { EmptyState } from "@/components/common/EmptyState";
import { useFileUploads } from "@/features/files/api";
import { useTaxonomy } from "@/features/home/api";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";
import { cn } from "@/lib/utils";

// ===== /gv/bai-tap/:id — tabs: Câu hỏi · Cài đặt · Hiển thị · Giao cho lớp · Kết quả · Thống kê (spec §5.2) =====

type TabId =
  | "cau-hoi"
  | "cai-dat"
  | "hien-thi"
  | "giao-lop"
  | "ket-qua"
  | "thong-ke";

export function GvBaiTapFormPage() {
  const { id: idParam } = useParams();
  const id = Number(idParam);
  const validId = Number.isInteger(id) && id > 0 ? id : null;
  const [searchParams] = useSearchParams();
  const imported = searchParams.get("imported") === "1";

  const { data, isPending, isError, error } = useMyQuiz(validId);
  // tab giữ ở component ngoài (không bị remount theo key updatedAt) để
  // đổi trạng thái Hiển thị/Hẹn giờ không làm mất tab GV đang xem.
  const [tab, setTab] = useState<TabId>("cau-hoi");

  if (!validId)
    return (
      <EmptyState
        title="Không hợp lệ"
        description="Đường dẫn bài tập không đúng định dạng."
      />
    );
  if (isPending)
    return (
      <div className="mx-auto max-w-6xl space-y-4 px-4 py-6">
        <Skeleton className="h-9 w-72" />
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-96 w-full" />
      </div>
    );
  if (isError || !data)
    return (
      <EmptyState
        title="Không tải được bài tập"
        description={error instanceof Error ? error.message : undefined}
      />
    );
  if (data.isDeleted)
    return (
      <EmptyState
        title="Bài tập đã bị xóa"
        description="Bài tập đang ở trong thùng rác."
      />
    );

  return (
    <QuizFormInner
      // updatedAt trong key → sau mỗi lần lưu (BE bump UpdatedAt) form
      // remount từ dữ liệu mới: id tạm của câu/nhóm mới thành id thật.
      key={`${data.id}:${data.updatedAt}`}
      data={data}
      importedBanner={imported}
      tab={tab}
      setTab={setTab}
    />
  );
}

function parseWarnings(raw: string | null): QuizWarning[] {
  if (!raw) return [];
  try {
    const v = JSON.parse(raw);
    return Array.isArray(v) ? (v as QuizWarning[]) : [];
  } catch {
    return [];
  }
}

const SCOPE_LABELS: Record<Scope, string> = {
  Public: "Công khai",
  Teachers: "Giáo viên",
  Team: "Trong tổ",
  Private: "Riêng tư",
};

function QuizFormInner({
  data,
  importedBanner,
  tab,
  setTab,
}: {
  data: QuizDetail;
  importedBanner: boolean;
  tab: TabId;
  setTab: (t: TabId) => void;
}) {
  const qc = useQueryClient();
  const { data: tax } = useTaxonomy();
  const updateQuiz = useUpdateQuiz();
  const publish = usePublishQuiz();
  const files = useFileUploads(1);

  const [dirty, setDirty] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [conflictBanner, setConflictBanner] = useState(false);
  const [regradeCount, setRegradeCount] = useState<number | null>(null);
  const [previewOpen, setPreviewOpen] = useState(false);

  // --- Thông tin chung ---
  const [title, setTitle] = useState(data.title);
  const [sectionId, setSectionId] = useState(data.sectionId ?? 0);
  const [gradeId, setGradeId] = useState(data.gradeId ?? 0);
  const [subjectId, setSubjectId] = useState(data.subjectId ?? 0);
  const [schoolYearId, setSchoolYearId] = useState(data.schoolYearId ?? 0);
  const [weekNo, setWeekNo] = useState(data.weekNo ?? 0);
  const [scope, setScope] = useState<Scope>(data.scope as Scope);
  const [descText, setDescText] = useState(() =>
    htmlToText(data.descriptionHtml),
  );
  const descHtmlOrig = data.descriptionHtml ?? "";
  const [printFileId, setPrintFileId] = useState<number | null>(
    data.printFileId,
  );

  // --- Cài đặt làm bài ---
  const [timeLimit, setTimeLimit] = useState(data.timeLimitMinutes ?? 0);
  const [shuffleQuestions, setShuffleQuestions] = useState(
    data.shuffleQuestions,
  );
  const [shuffleOptions, setShuffleOptions] = useState(data.shuffleOptions);
  const [showAnswers, setShowAnswers] = useState<ShowAnswers>(
    data.showAnswers as ShowAnswers,
  );
  const [identityMode, setIdentityMode] = useState<IdentityMode>(
    data.identityMode as IdentityMode,
  );
  const [maxAttempts, setMaxAttempts] = useState(data.maxAttempts ?? 0);
  const [multiScoring, setMultiScoring] = useState<MultiScoring>(
    data.multiScoring as MultiScoring,
  );
  const [scoreRounding, setScoreRounding] = useState<ScoreRounding>(
    data.scoreRounding as ScoreRounding,
  );
  const [classOnly, setClassOnly] = useState(data.classOnly);

  // --- Câu hỏi & nhóm ---
  const warnings = useMemo(
    () => parseWarnings(data.importWarnings),
    [data.importWarnings],
  );
  const [groups, setGroups] = useState<EditorGroup[]>(() =>
    data.groups.map((g) => ({
      id: g.id,
      sort: g.sort,
      title: g.title ?? "",
      passageText: htmlToText(g.passageHtml),
      passageHtmlOrig: g.passageHtml ?? "",
    })),
  );
  const [questions, setQuestions] = useState<EditorQuestion[]>(() =>
    data.questions.map((q) => ({
      id: q.id,
      sort: q.sort,
      groupId: q.groupId,
      type: q.type as EditorQuestion["type"],
      contentText: htmlToText(q.contentHtml),
      contentHtmlOrig: q.contentHtml,
      explanationText: htmlToText(q.explanationHtml),
      explanationHtmlOrig: q.explanationHtml ?? "",
      points: q.points,
      options: q.options.map((o) => ({
        id: o.id,
        sort: o.sort,
        text: htmlToText(o.contentHtml),
        htmlOrig: o.contentHtml,
        isCorrect: o.isCorrect,
      })),
      hasWarning: warnings.some((w) => w.questionNumber === q.sort),
    })),
  );

  const touch = () => setDirty(true);

  // Cảnh báo khi rời trang khi có thay đổi chưa lưu.
  useEffect(() => {
    if (!dirty) return;
    const h = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener("beforeunload", h);
    return () => window.removeEventListener("beforeunload", h);
  }, [dirty]);

  const quizSections = useMemo(
    () => (tax?.sections ?? []).filter((s) => s.contentKind !== "Document"),
    [tax],
  );
  const selectedSection = quizSections.find((s) => s.id === sectionId);
  const requireWeek = selectedSection?.requireWeek ?? false;
  const internalSection = selectedSection?.isInternal ?? false;

  // ===== Lưu (PUT thay toàn bộ, spec §6.5) =====

  const buildBody = (): UpdateQuizRequest => {
    // Nhóm mới (id tạm âm) được tham chiếu bằng -1..-N theo thứ tự gửi.
    const sorted = [...groups].sort((a, b) => a.sort - b.sort);
    const tempMap = new Map<number, number>();
    let n = 0;
    for (const g of sorted)
      if (isTempId(g.id)) tempMap.set(g.id as number, ++n);
    const groupRef = (gid: number | null | undefined): number | null => {
      if (gid == null) return null;
      if (gid > 0) return gid;
      return tempMap.get(gid) != null ? -tempMap.get(gid)! : null;
    };

    const settings: QuizSettings = {
      timeLimitMinutes: timeLimit > 0 ? timeLimit : null,
      shuffleQuestions,
      shuffleOptions,
      showAnswers,
      identityMode,
      maxAttempts: maxAttempts > 0 ? maxAttempts : null,
      multiScoring,
      scoreRounding,
      classOnly,
    };
    return {
      title: title.trim() || null,
      sectionId: sectionId || null,
      gradeId: gradeId || null,
      subjectId: subjectId || null,
      schoolYearId: schoolYearId || null,
      weekNo: weekNo || null,
      scope,
      descriptionHtml: textToHtml(descText, descHtmlOrig) || null,
      printFileId,
      settings,
      groups: sorted.map((g) => ({
        id: isTempId(g.id) ? null : g.id,
        sort: g.sort,
        title: g.title.trim() || null,
        passageHtml: textToHtml(g.passageText, g.passageHtmlOrig) || null,
      })),
      questions: [...questions]
        .sort((a, b) => a.sort - b.sort)
        .map((q) => ({
          id: isTempId(q.id) ? null : q.id,
          sort: q.sort,
          groupId: groupRef(q.groupId),
          type: q.type,
          contentHtml: textToHtml(q.contentText, q.contentHtmlOrig),
          explanationHtml:
            textToHtml(q.explanationText, q.explanationHtmlOrig) || null,
          points: q.points,
          options: [...q.options]
            .sort((a, b) => a.sort - b.sort)
            .map((o) => ({
              id: isTempId(o.id) ? null : o.id,
              sort: o.sort,
              contentHtml: textToHtml(o.text, o.htmlOrig),
              isCorrect: o.isCorrect,
            })),
        })),
      updatedAt: data.updatedAt,
    };
  };

  const doSave = async (confirmRegrade: boolean) => {
    setSaveError(null);
    setConflictBanner(false);
    try {
      await updateQuiz.mutateAsync({
        id: data.id,
        body: buildBody(),
        confirmRegrade,
      });
      setRegradeCount(null);
      setDirty(false);
    } catch (e) {
      const d = (
        e as {
          data?: {
            code?: string;
            affectedAttempts?: number;
            errors?: Record<string, string[]>;
          };
        }
      )?.data;
      if (d?.code === "quiz.has_attempts") {
        setRegradeCount(d.affectedAttempts ?? 0);
      } else if (d?.code === "conflict") {
        // Người khác đã sửa → tải lại dữ liệu mới (key đổi → remount).
        void qc.invalidateQueries({ queryKey: ["teacher", "quiz", data.id] });
      } else {
        const fields = d?.errors ?? {};
        const keys = Object.keys(fields);
        if (keys.length > 0) {
          setSaveError(keys.map((k) => fields[k].join(" ")).join(" · "));
          if (
            ["title", "sectionId", "gradeId", "weekNo", "scope"].some(
              (k) => k in fields,
            )
          )
            setTab("cai-dat");
        } else {
          setSaveError(
            e instanceof Error ? e.message : "Không lưu được bài tập",
          );
        }
      }
    }
  };

  // ===== Xem như học sinh =====

  const buildPreview = (): AttemptRunnerData => {
    const sortedG = [...groups].sort((a, b) => a.sort - b.sort);
    return {
      groups: sortedG.map((g, i) => ({
        id: g.id ?? -1000 - i,
        title: g.title || null,
        passageHtml: textToHtml(g.passageText, g.passageHtmlOrig) || null,
      })),
      questions: [...questions]
        .sort((a, b) => a.sort - b.sort)
        .map((q, i) => ({
          id: q.id ?? -2000 - i,
          groupId: q.groupId ?? null,
          number: q.sort,
          type: q.type,
          contentHtml: textToHtml(q.contentText, q.contentHtmlOrig),
          options: [...q.options]
            .sort((a, b) => a.sort - b.sort)
            .map((o, i) => ({
              id: o.id ?? -3000 - i,
              contentHtml: textToHtml(o.text, o.htmlOrig),
            })),
        })),
      answers: [],
    };
  };

  const tabs: { id: TabId; label: string }[] = [
    { id: "cau-hoi", label: `Câu hỏi (${questions.length})` },
    { id: "cai-dat", label: "Cài đặt" },
    { id: "hien-thi", label: "Hiển thị" },
    { id: "giao-lop", label: "Giao cho lớp" },
    { id: "ket-qua", label: "Kết quả" },
    { id: "thong-ke", label: "Thống kê" },
  ];

  const readyFile = files.items.find((it) => it.status === "ready" && it.file);

  return (
    <div className="mx-auto max-w-6xl px-4 py-6">
      {/* Header */}
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <Link2List />
        <h1 className="min-w-0 flex-1 truncate text-xl font-bold">
          {title || "Bài tập (chưa có tiêu đề)"}
        </h1>
        {dirty ? (
          <Badge className="bg-warn/10 text-warn">Chưa lưu</Badge>
        ) : null}
        <span className="hidden text-xs text-muted sm:inline">
          Cập nhật {formatDateTime(data.updatedAt)}
        </span>
        <Button
          onClick={() => void doSave(false)}
          disabled={updateQuiz.isPending}
        >
          <Save className="size-4" aria-hidden />
          {updateQuiz.isPending ? "Đang lưu…" : "Lưu"}
        </Button>
      </div>

      {importedBanner ? (
        <div className="mb-4 flex flex-wrap items-center gap-2 rounded-card border border-violet/30 bg-violet/5 p-3 text-sm">
          <span>
            Đã tạo bài tập từ file — hãy rà soát các câu trước khi mở cho học
            sinh.
          </span>
          <Button
            size="sm"
            variant="outline"
            className="ml-auto"
            onClick={() => setTab("cau-hoi")}
          >
            Xem câu hỏi
          </Button>
        </div>
      ) : null}

      {conflictBanner ? (
        <div className="mb-4 rounded-card border border-warn/40 bg-warn/10 p-3 text-sm text-warn">
          Bài tập đã được thay đổi sau lần tải cuối. Form đã cập nhật từ dữ liệu
          mới — hãy kiểm tra rồi lưu lại.
        </div>
      ) : null}

      {saveError ? (
        <div
          role="alert"
          className="mb-4 rounded-card border border-redpen/40 bg-redpen/10 p-3 text-sm text-redpen"
        >
          {saveError}
        </div>
      ) : null}

      {/* Tabs */}
      <div
        className="mb-4 flex flex-wrap gap-1 border-b border-grid"
        role="tablist"
        aria-label="Các tab bài tập"
      >
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            onClick={() => setTab(t.id)}
            className={cn(
              "-mb-px border-b-2 px-3 py-2 text-sm transition",
              tab === t.id
                ? "border-violet font-medium text-violet"
                : "border-transparent text-muted hover:text-ink",
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "cau-hoi" ? (
        <QuestionsTab
          questions={questions}
          setQuestions={(u) => {
            setQuestions(u);
            touch();
          }}
          groups={groups}
          setGroups={(u) => {
            setGroups(u);
            touch();
          }}
          warnings={warnings}
          onPreview={() => setPreviewOpen(true)}
        />
      ) : null}

      {tab === "cai-dat" ? (
        <div className="max-w-2xl space-y-6">
          <section className="rounded-card border border-grid bg-white p-4">
            <h2 className="mb-3 text-sm font-semibold">Thông tin chung</h2>
            <div className="space-y-3">
              <Field label="Tiêu đề *">
                <input
                  type="text"
                  value={title}
                  maxLength={300}
                  onChange={(e) => {
                    setTitle(e.target.value);
                    touch();
                  }}
                  className="h-9 w-full rounded-btn border border-grid px-2.5 text-sm outline-none focus:border-violet"
                />
              </Field>
              <div className="grid gap-3 sm:grid-cols-2">
                <Field label="Chuyên mục *">
                  <select
                    value={sectionId}
                    onChange={(e) => {
                      setSectionId(Number(e.target.value));
                      touch();
                    }}
                    className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                  >
                    {quizSections.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field
                  label={internalSection ? "Khối (không bắt buộc)" : "Khối *"}
                >
                  <select
                    value={gradeId}
                    onChange={(e) => {
                      setGradeId(Number(e.target.value));
                      touch();
                    }}
                    className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                  >
                    <option value={0}>—</option>
                    {(tax?.grades ?? []).map((g) => (
                      <option key={g.id} value={g.id}>
                        Khối {g.name}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Môn học">
                  <select
                    value={subjectId}
                    onChange={(e) => {
                      setSubjectId(Number(e.target.value));
                      touch();
                    }}
                    className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                  >
                    <option value={0}>—</option>
                    {(tax?.subjects ?? []).map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Năm học">
                  <select
                    value={schoolYearId}
                    onChange={(e) => {
                      setSchoolYearId(Number(e.target.value));
                      touch();
                    }}
                    className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                  >
                    <option value={0}>—</option>
                    {(tax?.schoolYears ?? []).map((y) => (
                      <option key={y.id} value={y.id}>
                        {y.name}
                        {y.isCurrent ? " (hiện tại)" : ""}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field
                  label={requireWeek ? "Tuần *" : "Tuần (chỉ BT cuối tuần)"}
                >
                  <input
                    type="number"
                    min={1}
                    max={37}
                    value={weekNo || ""}
                    placeholder="1–37"
                    onChange={(e) => {
                      setWeekNo(Number(e.target.value) || 0);
                      touch();
                    }}
                    className="h-9 w-full rounded-btn border border-grid px-2 text-sm tabular-nums outline-none focus:border-violet"
                  />
                </Field>
                <Field label="Phạm vi xem">
                  <select
                    value={scope}
                    onChange={(e) => {
                      setScope(e.target.value as Scope);
                      touch();
                    }}
                    className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                  >
                    {(Object.keys(SCOPE_LABELS) as Scope[]).map((s) => (
                      <option key={s} value={s}>
                        {SCOPE_LABELS[s]}
                      </option>
                    ))}
                  </select>
                </Field>
              </div>
              <Field label="Mô tả bài (tùy chọn)">
                <textarea
                  value={descText}
                  rows={3}
                  onChange={(e) => {
                    setDescText(e.target.value);
                    touch();
                  }}
                  placeholder="Hướng dẫn ngắn cho học sinh trước khi bắt đầu…"
                  className="w-full rounded-btn border border-grid p-2.5 text-sm outline-none focus:border-violet"
                />
              </Field>
            </div>
          </section>

          <section className="rounded-card border border-grid bg-white p-4">
            <h2 className="mb-3 text-sm font-semibold">Cài đặt làm bài</h2>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Hiển thị bài tập cho">
                <select
                  value={classOnly ? "class" : "everyone"}
                  onChange={(e) => {
                    setClassOnly(e.target.value === "class");
                    touch();
                  }}
                  className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                >
                  <option value="everyone">
                    Mọi người (hiện ở trang chủ khi Hiện/Hẹn giờ)
                  </option>
                  <option value="class">
                    Chỉ học sinh lớp đã giao bài (qua mã/QR)
                  </option>
                </select>
              </Field>
              <Field label="Thời gian làm (phút, trống = không giới hạn)">
                <input
                  type="number"
                  min={0}
                  value={timeLimit || ""}
                  placeholder="Không giới hạn"
                  onChange={(e) => {
                    setTimeLimit(Number(e.target.value) || 0);
                    touch();
                  }}
                  className="h-9 w-full rounded-btn border border-grid px-2 text-sm tabular-nums outline-none focus:border-violet"
                />
              </Field>
              <Field label="Số lượt tối đa (trống = không giới hạn)">
                <input
                  type="number"
                  min={0}
                  value={maxAttempts || ""}
                  placeholder="Không giới hạn"
                  onChange={(e) => {
                    setMaxAttempts(Number(e.target.value) || 0);
                    touch();
                  }}
                  className="h-9 w-full rounded-btn border border-grid px-2 text-sm tabular-nums outline-none focus:border-violet"
                />
              </Field>
              <Field label="Hiện đáp án">
                <select
                  value={showAnswers}
                  onChange={(e) => {
                    setShowAnswers(e.target.value as ShowAnswers);
                    touch();
                  }}
                  className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                >
                  <option value="Never">Không bao giờ</option>
                  <option value="AfterSubmit">Sau khi nộp</option>
                  <option value="AfterClose">Sau khi đóng bài</option>
                </select>
              </Field>
              <Field label="Nhận diện học sinh">
                <select
                  value={identityMode}
                  onChange={(e) => {
                    setIdentityMode(e.target.value as IdentityMode);
                    touch();
                  }}
                  className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                >
                  <option value="Anonymous">Khách (không ghi nhận)</option>
                  <option value="Name">Nhập họ tên</option>
                  <option value="NameAndClass">Họ tên + lớp</option>
                </select>
              </Field>
              <Field label="Chấm câu chọn nhiều đáp án">
                <select
                  value={multiScoring}
                  onChange={(e) => {
                    setMultiScoring(e.target.value as MultiScoring);
                    touch();
                  }}
                  className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                >
                  <option value="AllOrNothing">Đủ đáp án mới được điểm</option>
                  <option value="Partial">Tính từng phần</option>
                </select>
              </Field>
              <Field label="Làm tròn điểm">
                <select
                  value={scoreRounding}
                  onChange={(e) => {
                    setScoreRounding(e.target.value as ScoreRounding);
                    touch();
                  }}
                  className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                >
                  <option value="None">Không làm tròn</option>
                  <option value="Quarter">Gần 0,25</option>
                  <option value="Half">Gần 0,5</option>
                  <option value="Integer">Gần số nguyên</option>
                </select>
              </Field>
              <label className="flex items-center gap-2 self-end pb-2 text-sm">
                <input
                  type="checkbox"
                  checked={shuffleQuestions}
                  onChange={(e) => {
                    setShuffleQuestions(e.target.checked);
                    touch();
                  }}
                  className="size-4 accent-violet"
                />
                Trộn thứ tự câu
              </label>
              <label className="flex items-center gap-2 self-end pb-2 text-sm">
                <input
                  type="checkbox"
                  checked={shuffleOptions}
                  onChange={(e) => {
                    setShuffleOptions(e.target.checked);
                    touch();
                  }}
                  className="size-4 accent-violet"
                />
                Trộn thứ tự phương án
              </label>
            </div>
          </section>

          <section className="rounded-card border border-grid bg-white p-4">
            <h2 className="mb-1 text-sm font-semibold">
              File đề in kèm (tùy chọn)
            </h2>
            <p className="mb-3 text-xs text-muted">
              File PDF/Word của đề để học sinh có thể tải về làm giấy.
            </p>
            {printFileId != null ? (
              <div className="flex items-center gap-2 rounded-card border border-grid bg-paper p-2.5 text-sm">
                <a
                  href={`/api/files/${printFileId}/download`}
                  className="font-medium text-violet hover:underline"
                >
                  Tải file đề in
                </a>
                <button
                  type="button"
                  onClick={() => {
                    setPrintFileId(null);
                    touch();
                  }}
                  className="ml-auto text-xs text-muted hover:text-redpen"
                >
                  Bỏ
                </button>
              </div>
            ) : null}
            <div className={cn(printFileId != null && "mt-3")}>
              <FileDropzone
                maxFiles={1}
                items={files.items}
                onAdd={(f) => void files.addFiles(f)}
                onRemove={files.remove}
              />
            </div>
            {readyFile?.file && readyFile.file.id !== printFileId ? (
              <Button
                size="sm"
                variant="outline"
                className="mt-2"
                onClick={() => {
                  setPrintFileId(readyFile.file!.id);
                  touch();
                }}
              >
                Dùng làm file đề in
              </Button>
            ) : null}
          </section>
        </div>
      ) : null}

      {tab === "hien-thi" ? (
        <div className="max-w-md space-y-4">
          <div className="rounded-card border border-grid bg-white p-4">
            <h2 className="mb-3 text-sm font-semibold">Trạng thái hiển thị</h2>
            <PublishControl
              publishState={data.publishState}
              publishFrom={data.publishFrom}
              publishUntil={data.publishUntil}
              busy={publish.isPending}
              publish={async (req) => {
                await publish.mutateAsync({ id: data.id, body: req });
              }}
            />
            <p className="mt-3 text-sm text-muted">
              {publishStateLabel(data.publishState)}
              {data.publishFrom
                ? ` · từ ${formatDateTime(data.publishFrom)}`
                : ""}
              {data.publishUntil
                ? ` · đến ${formatDateTime(data.publishUntil)}`
                : ""}
            </p>
            <p className="mt-1 text-xs text-muted">
              Bài tập đang được tạo ở chế độ ẩn — học sinh chỉ làm được khi bạn
              bấm “Hiện” hoặc hẹn giờ, hoặc khi giao cho lớp (mã/QR).
            </p>
            {data.classOnly ? (
              <p className="mt-2 text-xs text-violet">
                Bài tập chỉ dành cho học sinh của lớp đã giao bài (qua mã/QR) —
                không hiện ở trang chủ và khu công khai.
              </p>
            ) : null}
          </div>
          <div className="rounded-card border border-grid bg-white p-4">
            <h2 className="mb-2 text-sm font-semibold">Phạm vi xem</h2>
            <select
              value={scope}
              onChange={(e) => {
                setScope(e.target.value as Scope);
                touch();
              }}
              className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            >
              {(Object.keys(SCOPE_LABELS) as Scope[]).map((s) => (
                <option key={s} value={s}>
                  {SCOPE_LABELS[s]}
                </option>
              ))}
            </select>
            <p className="mt-1.5 text-xs text-muted">
              Công khai: mọi người xem được. Giáo viên: chỉ GV đăng nhập. Trong
              tổ: chỉ thành viên tổ. Riêng tư: chỉ bạn (và Admin).
            </p>
          </div>
          {data.moderationStatus === "PendingReview" ? (
            <div className="rounded-card border border-warn/40 bg-warn/10 p-3 text-sm text-warn">
              Nội dung đang chờ kiểm duyệt
              {data.moderationNote ? `: ${data.moderationNote}` : "."}
            </div>
          ) : null}
        </div>
      ) : null}

      {tab === "giao-lop" ? <GiaoLopTab quizId={data.id} /> : null}

      {tab === "ket-qua" ? <ResultsTab quizId={data.id} /> : null}

      {tab === "thong-ke" ? <StatsTab quizId={data.id} /> : null}

      {/* Xem như học sinh */}
      {previewOpen ? (
        <div
          className="fixed inset-0 z-50 overflow-y-auto bg-ink/50 p-0 sm:p-6"
          role="dialog"
          aria-modal="true"
          aria-label="Xem trước như học sinh"
          onClick={(e) => {
            if (e.target === e.currentTarget) setPreviewOpen(false);
          }}
        >
          <div className="mx-auto max-w-2xl sm:rounded-card sm:border sm:border-grid sm:bg-white">
            <div className="flex items-center justify-between border-b border-grid bg-white px-4 py-2.5">
              <span className="text-sm font-medium text-muted">
                Xem như học sinh — chế độ xem trước, không lưu
              </span>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPreviewOpen(false)}
              >
                <X className="size-4" aria-hidden /> Đóng
              </Button>
            </div>
            <AttemptRunner
              attemptId={null}
              title={title || "Bài tập"}
              data={buildPreview()}
              expiresAt={null}
              onFinished={() => setPreviewOpen(false)}
            />
          </div>
        </div>
      ) : null}

      {/* Xác nhận chấm lại (spec §6.1) */}
      {regradeCount != null ? (
        <div
          className="fixed inset-0 z-50 grid place-items-center bg-ink/50 p-4"
          role="dialog"
          aria-modal="true"
          aria-label="Xác nhận chấm lại"
        >
          <div className="w-full max-w-md rounded-card border border-grid bg-white p-4">
            <h3 className="mb-2 font-semibold">Chấm lại các lượt đã nộp?</h3>
            <p className="text-sm text-muted">
              Bài tập đã có {regradeCount} lượt làm. Việc đổi đáp án đúng, xóa
              câu hoặc xóa phương án sẽ chấm lại toàn bộ lượt làm theo dữ liệu
              mới.
            </p>
            <div className="mt-4 flex justify-end gap-2">
              <Button
                variant="outline"
                onClick={() => setRegradeCount(null)}
                disabled={updateQuiz.isPending}
              >
                Hủy
              </Button>
              <Button
                onClick={() => void doSave(true)}
                disabled={updateQuiz.isPending}
              >
                {updateQuiz.isPending ? "Đang xử lý…" : "Chấm lại"}
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function Link2List() {
  const navigate = useNavigate();
  return (
    <button
      type="button"
      aria-label="Quay lại danh sách bài tập"
      onClick={() => navigate("/gv/bai-tap")}
      className="rounded-btn border border-grid bg-white p-2 text-muted hover:border-violet hover:text-violet"
    >
      <ArrowLeft className="size-4" aria-hidden />
    </button>
  );
}

/** Tab "Giao cho lớp": chọn lớp → bảng bài giao + mã/QR (spec §5.2, §7). */
function GiaoLopTab({ quizId }: { quizId: number }) {
  const { data: classes, isPending } = useMyClasses();
  const [classId, setClassId] = useState(0);

  // Tự chọn lớp đầu tiên khi danh sách về.
  useEffect(() => {
    if (classId === 0 && (classes ?? []).length > 0)
      setClassId((classes ?? [])[0].id);
  }, [classes, classId]);

  if (isPending)
    return (
      <div className="space-y-2">
        <Skeleton className="h-9 w-64" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  if ((classes ?? []).length === 0)
    return (
      <EmptyState
        title="Chưa có lớp nào"
        description="Tạo lớp ở mục Lớp học trước, rồi quay lại đây giao bài."
        action={
          <Link to="/gv/lop" className="text-sm text-violet hover:underline">
            Tạo lớp
          </Link>
        }
      />
    );

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <label className="block">
          <span className="sr-only">Chọn lớp để giao bài</span>
          <select
            value={classId}
            onChange={(e) => setClassId(Number(e.target.value))}
            className="h-9 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            {(classes ?? []).map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <Link
          to={`/gv/lop/${classId}`}
          className="text-xs text-violet hover:underline"
        >
          Xem chi tiết lớp →
        </Link>
      </div>
      {classId !== 0 ? (
        <ClassAssignmentsPanel
          key={classId}
          classId={classId}
          quizId={quizId}
        />
      ) : null}
    </div>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-muted">{label}</span>
      {children}
    </label>
  );
}

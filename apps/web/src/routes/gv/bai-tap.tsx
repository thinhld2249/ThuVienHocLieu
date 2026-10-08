import { useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import {
  AlertTriangle,
  Copy,
  Download,
  FileSpreadsheet,
  FileText,
  Pencil,
  Plus,
  RotateCcw,
  Search,
  Trash2,
  Upload,
} from "lucide-react";
import { useTaxonomy } from "@/features/home/api";
import {
  importQuiz,
  useCreateQuiz,
  useDeleteQuiz,
  useDuplicateQuiz,
  useMyQuizzes,
  usePublishQuiz,
  useRestoreQuiz,
} from "@/features/quizzes/api";
import {
  PublishControl,
} from "@/components/common/PublishControl";
import { Pagination } from "@/components/common/Pagination";
import { EmptyState } from "@/components/common/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";
import { cn } from "@/lib/utils";

const PAGE_SIZE = 20;

/** Modal tạo bài tập trống (chỉ trường bắt buộc: tiêu đề, chuyên mục, khối). */
function CreateBlankModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (id: number) => void;
}) {
  const { data: taxonomy } = useTaxonomy();
  const create = useCreateQuiz();
  const [title, setTitle] = useState("");
  const [sectionId, setSectionId] = useState<number | "">("");
  const [gradeId, setGradeId] = useState<number | "">("");
  const [error, setError] = useState<string | null>(null);

  const quizSections = (taxonomy?.sections ?? []).filter(
    (s) => s.contentKind !== "Document" && !s.isInternal,
  );
  const section = quizSections.find((s) => s.id === sectionId);

  const submit = async () => {
    setError(null);
    try {
      const { id } = await create.mutateAsync({
        title: title.trim(),
        sectionId: sectionId === "" ? null : sectionId,
        gradeId: gradeId === "" ? null : gradeId,
      });
      onCreated(id);
    } catch (e) {
      const errors = (e as { data?: { errors?: Record<string, string[]> } })
        ?.data?.errors;
      setError(
        errors
          ? Object.values(errors).flat().join(" ")
          : e instanceof Error
            ? e.message
            : "Không tạo được bài tập",
      );
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label="Tạo bài tập mới"
    >
      <form
        className="w-full max-w-md rounded-card bg-white p-5"
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <h2 className="mb-3 font-semibold">Tạo bài tập mới</h2>
        <label className="mb-1 block text-sm font-medium">
          Tiêu đề (bắt buộc)
        </label>
        <input
          type="text"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          maxLength={300}
          placeholder="VD: Bài tập cuối tuần 5 – Toán"
          className="mb-3 h-10 w-full rounded-btn border border-grid px-3 text-sm outline-none focus:border-violet"
        />
        <label className="mb-1 block text-sm font-medium">
          Chuyên mục (bắt buộc)
        </label>
        <select
          value={sectionId}
          onChange={(e) =>
            setSectionId(e.target.value === "" ? "" : Number(e.target.value))
          }
          className="mb-3 h-10 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
        >
          <option value="">Chọn chuyên mục…</option>
          {quizSections.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </select>
        <label className="mb-1 block text-sm font-medium">
          Khối (bắt buộc)
        </label>
        <select
          value={gradeId}
          onChange={(e) =>
            setGradeId(e.target.value === "" ? "" : Number(e.target.value))
          }
          className="mb-3 h-10 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
        >
          <option value="">Chọn khối…</option>
          {(taxonomy?.grades ?? []).map((g) => (
            <option key={g.id} value={g.id}>
              Khối {g.name}
            </option>
          ))}
        </select>
        {section?.requireWeek ? (
          <p className="mb-3 text-xs text-warn">
            Chuyên mục này yêu cầu tuần — bổ sung trong trang chi tiết.
          </p>
        ) : null}
        {error ? (
          <p className="mb-3 text-sm text-redpen" role="alert">
            {error}
          </p>
        ) : null}
        <p className="mb-4 text-xs text-muted">
          Bài tập mới mặc định <strong>Đang ẩn</strong>. Bạn có thể sửa mọi
          trường sau khi tạo.
        </p>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Hủy
          </Button>
          <Button type="submit" disabled={create.isPending}>
            {create.isPending ? "Đang tạo…" : "Tạo"}
          </Button>
        </div>
      </form>
    </div>
  );
}

/** /gv/bai-tap — bảng bài tập của tôi (spec §5.2, §6). */
export function GvBaiTapPage() {
  const navigate = useNavigate();
  const { data: taxonomy } = useTaxonomy();
  const [q, setQ] = useState("");
  const [section, setSection] = useState("");
  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);
  const [importing, setImporting] = useState<null | "docx" | "xlsx">(null);
  const [importError, setImportError] = useState<string | null>(null);
  const docxInput = useRef<HTMLInputElement>(null);
  const xlsxInput = useRef<HTMLInputElement>(null);

  const { data, isPending } = useMyQuizzes({
    q: q.trim() || null,
    section: section || null,
    includeDeleted,
    page,
    pageSize: PAGE_SIZE,
  });
  const publish = usePublishQuiz();
  const del = useDeleteQuiz();
  const restore = useRestoreQuiz();
  const duplicate = useDuplicateQuiz();

  const rows = useMemo(() => data?.items ?? [], [data]);
  const quizSections = (taxonomy?.sections ?? []).filter(
    (s) => s.contentKind !== "Document",
  );

  const runImport = async (kind: "docx" | "xlsx", file: File) => {
    setImportError(null);
    setImporting(kind);
    try {
      const result = await importQuiz(kind, file);
      navigate(`/gv/bai-tap/${result.quizId}?imported=1`);
    } catch (e) {
      setImportError(
        e instanceof Error ? e.message : "Nhập file thất bại",
      );
    } finally {
      setImporting(null);
      if (docxInput.current) docxInput.current.value = "";
      if (xlsxInput.current) xlsxInput.current.value = "";
    }
  };

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Bài tập</h1>
        <div className="flex flex-wrap gap-2">
          <input
            ref={docxInput}
            type="file"
            accept=".docx"
            className="hidden"
            aria-hidden
            tabIndex={-1}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void runImport("docx", f);
            }}
          />
          <input
            ref={xlsxInput}
            type="file"
            accept=".xlsx"
            className="hidden"
            aria-hidden
            tabIndex={-1}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void runImport("xlsx", f);
            }}
          />
          <Button
            variant="outline"
            disabled={importing != null}
            onClick={() => docxInput.current?.click()}
          >
            <Upload className="size-4" aria-hidden />
            {importing === "docx" ? "Đang nhập…" : "Tạo từ Word"}
          </Button>
          <Button
            variant="outline"
            disabled={importing != null}
            onClick={() => xlsxInput.current?.click()}
          >
            <FileSpreadsheet className="size-4" aria-hidden />
            {importing === "xlsx" ? "Đang nhập…" : "Tạo từ Excel"}
          </Button>
          <Button onClick={() => setCreateOpen(true)}>
            <Plus className="size-4" aria-hidden /> Tạo trống
          </Button>
          <span className="flex items-center gap-2 text-xs text-muted">
            <a
              href="/templates/mau-bai-tap.docx"
              download
              className="inline-flex items-center gap-1 hover:text-violet"
            >
              <Download className="size-3.5" aria-hidden /> Mẫu Word
            </a>
            <a
              href="/templates/mau-bai-tap.xlsx"
              download
              className="inline-flex items-center gap-1 hover:text-violet"
            >
              <Download className="size-3.5" aria-hidden /> Mẫu Excel
            </a>
          </span>
        </div>
      </div>

      {importError ? (
        <p
          className="mb-3 rounded-card border border-redpen/30 bg-redpen/5 p-2 text-sm text-redpen"
          role="alert"
        >
          {importError}
        </p>
      ) : null}

      {/* Toolbar */}
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative">
          <Search
            className="absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted"
            aria-hidden
          />
          <input
            type="search"
            aria-label="Tìm bài tập của tôi"
            placeholder="Tìm theo tiêu đề…"
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
              setPage(1);
            }}
            className="h-9 w-56 rounded-btn border border-grid bg-white pl-8 pr-2 text-sm outline-none focus:border-violet"
          />
        </div>
        <select
          aria-label="Lọc chuyên mục"
          value={section}
          onChange={(e) => {
            setSection(e.target.value);
            setPage(1);
          }}
          className="h-9 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
        >
          <option value="">Chuyên mục: Tất cả</option>
          {quizSections.map((s) => (
            <option key={s.slug} value={s.slug}>
              {s.name}
            </option>
          ))}
        </select>
        <label className="flex h-9 cursor-pointer items-center gap-2 rounded-btn border border-grid bg-white px-3 text-sm">
          <input
            type="checkbox"
            checked={includeDeleted}
            onChange={(e) => {
              setIncludeDeleted(e.target.checked);
              setPage(1);
            }}
            className="accent-violet"
          />
          Xem thùng rác
        </label>
      </div>

      {/* Bảng */}
      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={FileText}
          title={includeDeleted ? "Thùng rác trống" : "Chưa có bài tập nào"}
          description={
            includeDeleted
              ? "Bài tập xóa sẽ nằm ở đây 30 ngày."
              : "Tạo từ file Word/Excel có sẵn hoặc tạo trống."
          }
          action={
            <Button
              size="sm"
              variant="secondary"
              onClick={() => docxInput.current?.click()}
            >
              <Upload className="size-4" aria-hidden /> Tạo từ file Word
            </Button>
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[900px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="p-2">Tiêu đề</th>
                <th className="p-2">Chuyên mục</th>
                <th className="p-2">Khối</th>
                <th className="p-2">Môn</th>
                <th className="p-2">Tuần</th>
                <th className="p-2 text-right">Câu</th>
                <th className="p-2 text-right">Lượt làm</th>
                <th className="p-2">Hiển thị</th>
                <th className="p-2">Tạo lúc</th>
                <th className="p-2 text-right">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr
                  key={r.id}
                  className="border-b border-grid/60 last:border-0 hover:bg-paper/50"
                >
                  <td className="max-w-64 p-2">
                    <button
                      type="button"
                      onClick={() => navigate(`/gv/bai-tap/${r.id}`)}
                      className={cn(
                        "line-clamp-2 text-left font-medium hover:text-violet",
                        r.isDeleted && "text-muted line-through",
                      )}
                    >
                      {r.title}
                    </button>
                    {r.hasImportWarnings ? (
                      <Badge className="mt-1 bg-warn/10 text-warn">
                        <AlertTriangle className="size-3" aria-hidden /> Cần
                        kiểm tra
                      </Badge>
                    ) : null}
                  </td>
                  <td className="p-2 text-muted">{r.sectionName ?? "—"}</td>
                  <td className="p-2 text-muted">
                    {r.grade != null ? r.grade : "—"}
                  </td>
                  <td className="p-2 text-muted">{r.subjectName ?? "—"}</td>
                  <td className="p-2 text-muted">
                    {r.weekNo != null ? r.weekNo : "—"}
                  </td>
                  <td className="p-2 text-right tabular-nums">
                    {r.questionCount}
                  </td>
                  <td className="p-2 text-right tabular-nums text-muted">
                    {r.attemptCount.toLocaleString("vi-VN")}
                  </td>
                  <td className="p-2">
                    {r.isDeleted ? (
                      <Badge className="bg-muted/10 text-muted">
                        Đã xóa
                      </Badge>
                    ) : (
                      <PublishControl
                        publishState={r.publishState}
                        publishFrom={r.publishFrom}
                        publishUntil={r.publishUntil}
                        busy={publish.isPending}
                        publish={async (req) => {
                          await publish.mutateAsync({ id: r.id, body: req });
                        }}
                      />
                    )}
                  </td>
                  <td className="p-2 text-muted">{formatDateTime(r.createdAt)}</td>
                  <td className="p-2">
                    <div className="flex justify-end gap-1">
                      {r.isDeleted ? (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={restore.isPending}
                          onClick={() => void restore.mutateAsync(r.id)}
                        >
                          <RotateCcw className="size-3.5" aria-hidden /> Khôi
                          phục
                        </Button>
                      ) : (
                        <>
                          <Button
                            size="sm"
                            variant="ghost"
                            title="Sửa"
                            aria-label={`Sửa ${r.title}`}
                            onClick={() => navigate(`/gv/bai-tap/${r.id}`)}
                          >
                            <Pencil className="size-3.5" aria-hidden />
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            title="Nhân bản"
                            aria-label={`Nhân bản ${r.title}`}
                            disabled={duplicate.isPending}
                            onClick={async () => {
                              const copy = await duplicate.mutateAsync(r.id);
                              navigate(`/gv/bai-tap/${copy.id}`);
                            }}
                          >
                            <Copy className="size-3.5" aria-hidden />
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            className="text-redpen hover:text-redpen"
                            title="Xóa"
                            aria-label={`Xóa ${r.title}`}
                            disabled={del.isPending}
                            onClick={() => {
                              if (
                                confirm(
                                  `Xóa "${r.title}"? Có thể khôi phục trong 30 ngày.`,
                                )
                              )
                                void del.mutateAsync(r.id);
                            }}
                          >
                            <Trash2 className="size-3.5" aria-hidden />
                          </Button>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <Pagination
        page={page}
        pageSize={PAGE_SIZE}
        total={data?.total ?? 0}
        onChange={setPage}
      />

      {createOpen ? (
        <CreateBlankModal
          onClose={() => setCreateOpen(false)}
          onCreated={(id) => navigate(`/gv/bai-tap/${id}`)}
        />
      ) : null}
    </div>
  );
}

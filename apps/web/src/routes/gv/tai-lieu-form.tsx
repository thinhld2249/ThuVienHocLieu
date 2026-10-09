import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import dayjs from "dayjs";
import { ArrowLeft, ImageIcon, Save, Trash2 } from "lucide-react";
import { useMe } from "@/features/auth/api";
import { useTaxonomy } from "@/features/home/api";
import {
  useCreateDocument,
  useMyDocument,
  useUpdateDocument,
} from "@/features/documents/api";
import type { ApiValidation } from "@/features/documents/types";
import { useFileUploads } from "@/features/files/api";
import { FileDropzone } from "@/components/common/FileDropzone";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { videoEmbedBlockForLine } from "@/lib/video-embed";
import { cn } from "@/lib/utils";

const inputCls =
  "h-9 w-full rounded-btn border border-grid bg-white px-2.5 text-sm outline-none focus:border-violet";
const labelCls = "mb-1 block text-sm font-medium";
const errCls = "mt-1 text-xs text-redpen";

/**
 * Escape HTML + tách dòng → <p> (mô tả tối giản, spec §5.2).
 * Dòng chỉ chứa link video (YouTube/Vimeo/Google Drive) → iframe nhúng.
 */
function plainToHtml(text: string): string | null {
  const trimmed = text.trim();
  if (!trimmed) return null;
  const esc = (s: string) =>
    s
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  return trimmed
    .split(/\n+/)
    .filter((l) => l.trim().length > 0)
    .map((l) => videoEmbedBlockForLine(l) ?? `<p>${esc(l.trim())}</p>`)
    .join("");
}

interface FormState {
  title: string;
  sectionId: string;
  gradeId: string;
  subjectId: string;
  schoolYearId: string;
  weekNo: string;
  scope: string;
  teamId: string;
  summary: string;
  description: string;
  coverFileId: string;
  allowGuestDownload: boolean;
  publishMode: "Visible" | "Hidden" | "Scheduled";
  publishFrom: string;
  publishUntil: string;
}

function isoLocal(v: string): string | null {
  return v ? dayjs(v).utc().toISOString() : null;
}

/** /gv/tai-lieu/moi + /gv/tai-lieu/:id — form tạo/sửa tài liệu (spec §5.2). */
export function TaiLieuFormPage() {
  const params = useParams();
  const docId = params.id ? Number(params.id) : null;
  const isEdit = docId != null && !Number.isNaN(docId);
  const navigate = useNavigate();
  const { data: me } = useMe();
  const { data: taxonomy } = useTaxonomy();
  const { data: detail, isPending: docPending } = useMyDocument(
    isEdit ? docId : null,
  );
  const uploads = useFileUploads();
  const create = useCreateDocument();
  const update = useUpdateDocument();

  const [form, setForm] = useState<FormState>({
    title: "",
    sectionId: "",
    gradeId: "",
    subjectId: "",
    schoolYearId: "",
    weekNo: "",
    scope: "Public",
    teamId: "",
    summary: "",
    description: "",
    coverFileId: "",
    allowGuestDownload: false,
    publishMode: "Visible",
    publishFrom: "",
    publishUntil: "",
  });
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  const [conflict, setConflict] = useState(false);
  const [removedFileIds, setRemovedFileIds] = useState<Set<number>>(new Set());
  const inited = useRef(false);

  // Mặc định năm học hiện tại (chạy khi taxonomy về).
  useEffect(() => {
    const current = taxonomy?.schoolYears.find((y) => y.isCurrent);
    if (current && !form.schoolYearId)
      setForm((f) => ({ ...f, schoolYearId: String(current.id) }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [taxonomy]);

  // Đổ dữ liệu chi tiết vào form khi sửa (chỉ 1 lần).
  useEffect(() => {
    if (!detail || inited.current) return;
    inited.current = true;
    setForm({
      title: detail.title,
      sectionId: detail.sectionId != null ? String(detail.sectionId) : "",
      gradeId: detail.gradeId != null ? String(detail.gradeId) : "",
      subjectId: detail.subjectId != null ? String(detail.subjectId) : "",
      schoolYearId:
        detail.schoolYearId != null ? String(detail.schoolYearId) : "",
      weekNo: detail.weekNo != null ? String(detail.weekNo) : "",
      scope: detail.scope,
      teamId: detail.teamId != null ? String(detail.teamId) : "",
      summary: detail.summary ?? "",
      description: detail.descriptionHtml
        ? detail.descriptionHtml
            .replace(/<\s*(br|\/p|\/div)[^>]*>/gi, "\n")
            .replace(/<[^>]+>/g, "")
            .replace(/&amp;/g, "&")
            .replace(/&lt;/g, "<")
            .replace(/&gt;/g, ">")
            .replace(/&quot;/g, '"')
        : "",
      coverFileId: detail.coverFileId != null ? String(detail.coverFileId) : "",
      allowGuestDownload: detail.allowGuestDownload,
      publishMode:
        detail.publishMode === "Hidden"
          ? "Hidden"
          : detail.publishMode === "Scheduled"
            ? "Scheduled"
            : "Visible",
      publishFrom: detail.publishFrom
        ? dayjs(detail.publishFrom).format("YYYY-MM-DDTHH:mm")
        : "",
      publishUntil: detail.publishUntil
        ? dayjs(detail.publishUntil).format("YYYY-MM-DDTHH:mm")
        : "",
    });
  }, [detail]);

  const section = useMemo(
    () => taxonomy?.sections.find((s) => s.id === Number(form.sectionId)),
    [taxonomy, form.sectionId],
  );
  const requireWeek = section?.requireWeek ?? false;

  // File hiện tại của tài liệu (chế độ sửa) trừ file đã bỏ.
  const existingFiles = useMemo(
    () => (detail?.files ?? []).filter((f) => !removedFileIds.has(f.id)),
    [detail, removedFileIds],
  );
  const uploadedFiles = useMemo(
    () =>
      uploads.items
        .filter((it) => it.file)
        .map((it) => ({ id: it.file!.id, name: it.name })),
    [uploads.items],
  );
  const fileOptions = useMemo(
    () => [
      ...existingFiles.map((f) => ({ id: f.id, name: f.originalName })),
      ...uploadedFiles,
    ],
    [existingFiles, uploadedFiles],
  );
  const fileIds = useMemo(
    () => [...existingFiles.map((f) => f.id), ...uploads.fileIds],
    [existingFiles, uploads.fileIds],
  );

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const submit = async () => {
    setFieldErrors({});
    setConflict(false);

    const errors: Record<string, string[]> = {};
    if (!form.title.trim()) errors.title = ["Tiêu đề là bắt buộc."];
    if (!form.sectionId) errors.sectionId = ["Chuyên mục là bắt buộc."];
    if (!form.gradeId) errors.gradeId = ["Khối là bắt buộc."];
    if (requireWeek && !form.weekNo)
      errors.weekNo = ["Tuần là bắt buộc với chuyên mục này."];
    if (form.scope === "Team" && !me) errors.teamId = ["Bạn chưa có tổ."];
    if (fileIds.length === 0) errors.fileIds = ["Tải lên ít nhất 1 file."];
    if (
      form.publishMode === "Scheduled" &&
      !form.publishFrom &&
      !form.publishUntil
    )
      errors.publishFrom = ["Hẹn giờ cần ít nhất một mốc Từ hoặc Đến."];
    if (
      form.publishMode === "Scheduled" &&
      form.publishFrom &&
      form.publishUntil &&
      dayjs(form.publishFrom).isBefore(dayjs(form.publishUntil)) === false
    )
      errors.publishUntil = ["Mốc Đến phải sau mốc Từ."];
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      return;
    }

    const body = {
      title: form.title.trim(),
      sectionId: Number(form.sectionId),
      gradeId: Number(form.gradeId),
      subjectId: form.subjectId ? Number(form.subjectId) : null,
      schoolYearId: form.schoolYearId ? Number(form.schoolYearId) : null,
      weekNo: form.weekNo ? Number(form.weekNo) : null,
      scope: form.scope,
      teamId: form.scope === "Team" && form.teamId ? Number(form.teamId) : null,
      summary: form.summary.trim() || null,
      descriptionHtml: plainToHtml(form.description),
      fileIds,
      coverFileId: form.coverFileId ? Number(form.coverFileId) : null,
      allowGuestDownload: form.allowGuestDownload,
      publishMode: form.publishMode,
      publishFrom:
        form.publishMode === "Scheduled" ? isoLocal(form.publishFrom) : null,
      publishUntil:
        form.publishMode === "Scheduled" ? isoLocal(form.publishUntil) : null,
    };

    try {
      if (isEdit) {
        await update.mutateAsync({
          id: docId!,
          body: { ...body, updatedAt: detail?.updatedAt ?? null },
        });
        navigate("/gv/tai-lieu");
      } else {
        const created = await create.mutateAsync(body);
        navigate(`/gv/tai-lieu/${created.id}`);
      }
    } catch (e) {
      const raw = (e as { data?: unknown })?.data;
      const v = raw as ApiValidation | undefined;
      if (v?.errors) {
        setFieldErrors(v.errors);
        return;
      }
      if (v?.code === "conflict") {
        setConflict(true);
        return;
      }
      alert(e instanceof Error ? e.message : "Không lưu được tài liệu");
    }
  };

  if (isEdit && docPending) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-96" />
      </div>
    );
  }
  if (isEdit && !detail) {
    return (
      <div className="py-16 text-center">
        <p className="text-lg font-medium">Không tìm thấy tài liệu</p>
        <p className="mt-1 text-sm text-muted">Nó có thể đã bị xóa.</p>
        <Link
          to="/gv/tai-lieu"
          className="mt-4 inline-block text-sm text-violet hover:underline"
        >
          ← Về danh sách tài liệu
        </Link>
      </div>
    );
  }

  const fieldError = (key: string) => fieldErrors[key]?.[0];

  return (
    <div className="mx-auto max-w-3xl">
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-xl font-semibold">
          {isEdit ? "Sửa tài liệu" : "Tài liệu mới"}
        </h1>
        <Link to="/gv/tai-lieu">
          <Button variant="ghost" size="sm">
            <ArrowLeft className="size-4" aria-hidden /> Danh sách
          </Button>
        </Link>
      </div>

      {conflict ? (
        <div
          role="alert"
          className="mb-4 rounded-card border border-redpen/40 bg-redpen/5 p-3 text-sm"
        >
          Tài liệu đã được thay đổi bởi người khác. Hãy tải lại trang và thử
          lại.
        </div>
      ) : null}

      <form
        className="space-y-5"
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        {/* Thông tin chính */}
        <div className="space-y-4 rounded-card border border-grid bg-white p-4">
          <div>
            <label htmlFor="f-title" className={labelCls}>
              Tiêu đề <span className="text-redpen">*</span>
            </label>
            <input
              id="f-title"
              type="text"
              value={form.title}
              onChange={(e) => set("title", e.target.value)}
              placeholder="VD: Kế hoạch bài dạy Toán 5 – Tuần 5"
              className={inputCls}
            />
            {fieldError("title") ? (
              <p className={errCls}>{fieldError("title")}</p>
            ) : null}
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="f-section" className={labelCls}>
                Chuyên mục <span className="text-redpen">*</span>
              </label>
              <select
                id="f-section"
                value={form.sectionId}
                onChange={(e) => set("sectionId", e.target.value)}
                className={inputCls}
              >
                <option value="">— Chọn chuyên mục —</option>
                {(taxonomy?.sections ?? [])
                  .filter((s) => s.contentKind !== "Quiz")
                  .map((s) => (
                    <option key={s.slug} value={s.id}>
                      {s.name}
                    </option>
                  ))}
              </select>
              {fieldError("sectionId") ? (
                <p className={errCls}>{fieldError("sectionId")}</p>
              ) : null}
            </div>
            <div>
              <label htmlFor="f-grade" className={labelCls}>
                Khối <span className="text-redpen">*</span>
              </label>
              <select
                id="f-grade"
                value={form.gradeId}
                onChange={(e) => set("gradeId", e.target.value)}
                className={inputCls}
              >
                <option value="">— Chọn khối —</option>
                {(taxonomy?.grades ?? []).map((g) => (
                  <option key={g.id} value={g.id}>
                    Khối {g.id}
                  </option>
                ))}
              </select>
              {fieldError("gradeId") ? (
                <p className={errCls}>{fieldError("gradeId")}</p>
              ) : null}
            </div>
            <div>
              <label htmlFor="f-subject" className={labelCls}>
                Môn học
              </label>
              <select
                id="f-subject"
                value={form.subjectId}
                onChange={(e) => set("subjectId", e.target.value)}
                className={inputCls}
              >
                <option value="">— Không chọn —</option>
                {(taxonomy?.subjects ?? []).map((s) => (
                  <option key={s.slug} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
              {fieldError("subjectId") ? (
                <p className={errCls}>{fieldError("subjectId")}</p>
              ) : null}
            </div>
            <div>
              <label htmlFor="f-year" className={labelCls}>
                Năm học
              </label>
              <select
                id="f-year"
                value={form.schoolYearId}
                onChange={(e) => set("schoolYearId", e.target.value)}
                className={inputCls}
              >
                {(taxonomy?.schoolYears ?? []).map((y) => (
                  <option key={y.id} value={y.id}>
                    {y.name}
                    {y.isCurrent ? " (hiện tại)" : ""}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="f-week" className={labelCls}>
                Tuần{" "}
                {requireWeek ? <span className="text-redpen">*</span> : null}
              </label>
              <select
                id="f-week"
                value={form.weekNo}
                onChange={(e) => set("weekNo", e.target.value)}
                className={inputCls}
              >
                <option value="">— Không chọn —</option>
                {Array.from({ length: 37 }, (_, i) => i + 1).map((w) => (
                  <option key={w} value={w}>
                    Tuần {w}
                  </option>
                ))}
              </select>
              {fieldError("weekNo") ? (
                <p className={errCls}>{fieldError("weekNo")}</p>
              ) : null}
            </div>
          </div>

          <div>
            <label htmlFor="f-summary" className={labelCls}>
              Tóm tắt
            </label>
            <textarea
              id="f-summary"
              value={form.summary}
              onChange={(e) => set("summary", e.target.value)}
              rows={2}
              placeholder="Vài dòng giới thiệu ngắn (hiện ở thẻ và trang chi tiết)"
              className="w-full rounded-btn border border-grid bg-white p-2.5 text-sm outline-none focus:border-violet"
            />
          </div>

          <div>
            <label htmlFor="f-desc" className={labelCls}>
              Mô tả chi tiết
            </label>
            <textarea
              id="f-desc"
              value={form.description}
              onChange={(e) => set("description", e.target.value)}
              rows={5}
              placeholder="Mỗi dòng là một đoạn."
              className="w-full rounded-btn border border-grid bg-white p-2.5 text-sm outline-none focus:border-violet"
            />
            <p className="mt-1 text-xs text-muted">
              Video: dán link YouTube / Google Drive / Vimeo vào một dòng riêng
              — hệ thống tự nhúng video vào trang tài liệu.
            </p>
          </div>
        </div>

        {/* File */}
        <div className="space-y-3 rounded-card border border-grid bg-white p-4">
          <h2 className="text-base font-semibold">File tài liệu</h2>
          {isEdit ? (
            <ul className="space-y-2">
              {existingFiles.map((f) => (
                <li
                  key={f.id}
                  className="flex items-center gap-3 rounded-btn border border-grid bg-white p-2"
                >
                  <span className="min-w-0 flex-1 truncate text-sm font-medium">
                    {f.originalName}
                  </span>
                  <span className="shrink-0 text-xs text-muted">
                    {f.processingStatus === "Ready"
                      ? "Sẵn sàng"
                      : f.processingStatus === "Failed"
                        ? "Lỗi preview"
                        : "Đang xử lý…"}
                  </span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-8 shrink-0"
                    aria-label={`Bỏ file ${f.originalName}`}
                    onClick={() =>
                      setRemovedFileIds((prev) => new Set(prev).add(f.id))
                    }
                  >
                    <Trash2 className="size-4" aria-hidden />
                  </Button>
                </li>
              ))}
            </ul>
          ) : null}
          <FileDropzone
            items={uploads.items}
            onAdd={(files) => void uploads.addFiles(files)}
            onRemove={uploads.remove}
          />
          {fieldError("fileIds") ? (
            <p className={errCls}>{fieldError("fileIds")}</p>
          ) : null}

          {fileOptions.length > 0 ? (
            <div className="pt-1">
              <label htmlFor="f-cover" className={labelCls}>
                Ảnh bìa (trang 1)
              </label>
              <select
                id="f-cover"
                value={form.coverFileId}
                onChange={(e) => set("coverFileId", e.target.value)}
                className={cn(inputCls, "max-w-sm")}
              >
                <option value="">
                  <ImageIcon className="inline size-3" aria-hidden /> Không chọn
                </option>
                {fileOptions.map((f) => (
                  <option key={f.id} value={f.id}>
                    {f.name}
                  </option>
                ))}
              </select>
              {fieldError("coverFileId") ? (
                <p className={errCls}>{fieldError("coverFileId")}</p>
              ) : null}
            </div>
          ) : null}

          <label className="flex cursor-pointer items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={form.allowGuestDownload}
              onChange={(e) => set("allowGuestDownload", e.target.checked)}
              className="accent-violet"
            />
            Cho phép khách (chưa đăng nhập) tải file
          </label>
        </div>

        {/* Phạm vi + hiển thị */}
        <div className="space-y-4 rounded-card border border-grid bg-white p-4">
          <h2 className="text-base font-semibold">Phạm vi &amp; hiển thị</h2>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="f-scope" className={labelCls}>
                Ai được xem
              </label>
              <select
                id="f-scope"
                value={form.scope}
                onChange={(e) => set("scope", e.target.value)}
                className={inputCls}
              >
                <option value="Public">Công khai (mọi người)</option>
                <option value="Teachers">Giáo viên</option>
                <option value="Team">Tổ của tôi</option>
                <option value="Private">Chỉ tôi</option>
              </select>
            </div>
            {form.scope === "Team" ? (
              <div>
                <label htmlFor="f-team" className={labelCls}>
                  Tổ (mặc định: tổ đầu tiên)
                </label>
                <select
                  id="f-team"
                  value={form.teamId}
                  onChange={(e) => set("teamId", e.target.value)}
                  className={inputCls}
                >
                  <option value="">— Tổ đầu tiên của bạn —</option>
                  {(me?.teams ?? []).map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </select>
                {fieldError("teamId") ? (
                  <p className={errCls}>{fieldError("teamId")}</p>
                ) : null}
              </div>
            ) : null}
          </div>

          <div>
            <span className={labelCls}>Hiển thị</span>
            <div className="flex flex-wrap items-center gap-2">
              {(
                [
                  ["Visible", "Hiện ngay"],
                  ["Hidden", "Đang ẩn"],
                  ["Scheduled", "Hẹn giờ"],
                ] as [FormState["publishMode"], string][]
              ).map(([m, label]) => (
                <button
                  key={m}
                  type="button"
                  onClick={() => set("publishMode", m)}
                  className={cn(
                    "rounded-btn border px-3 py-1.5 text-sm transition",
                    form.publishMode === m
                      ? "border-violet bg-violet text-white"
                      : "border-grid bg-white text-muted hover:border-violet/50",
                  )}
                >
                  {label}
                </button>
              ))}
            </div>
            {fieldError("publishMode") ? (
              <p className={errCls}>{fieldError("publishMode")}</p>
            ) : null}
          </div>

          {form.publishMode === "Scheduled" ? (
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <label htmlFor="f-from" className={labelCls}>
                  Mở từ
                </label>
                <input
                  id="f-from"
                  type="datetime-local"
                  value={form.publishFrom}
                  onChange={(e) => set("publishFrom", e.target.value)}
                  className={inputCls}
                />
                {fieldError("publishFrom") ? (
                  <p className={errCls}>{fieldError("publishFrom")}</p>
                ) : null}
              </div>
              <div>
                <label htmlFor="f-until" className={labelCls}>
                  Đóng đến (trống = không giới hạn)
                </label>
                <input
                  id="f-until"
                  type="datetime-local"
                  value={form.publishUntil}
                  onChange={(e) => set("publishUntil", e.target.value)}
                  className={inputCls}
                />
                {fieldError("publishUntil") ? (
                  <p className={errCls}>{fieldError("publishUntil")}</p>
                ) : null}
              </div>
            </div>
          ) : null}
        </div>

        {/* Submit */}
        <div className="flex items-center justify-end gap-3">
          <Link to="/gv/tai-lieu">
            <Button type="button" variant="ghost">
              Hủy
            </Button>
          </Link>
          <Button
            type="submit"
            disabled={uploads.busy || create.isPending || update.isPending}
          >
            <Save className="size-4" aria-hidden />
            {isEdit ? "Lưu thay đổi" : "Tạo tài liệu"}
          </Button>
        </div>
      </form>
    </div>
  );
}

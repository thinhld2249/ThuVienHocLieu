import { useRef, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router";
import {
  ArrowLeft,
  Download,
  Pencil,
  Plus,
  Trash2,
  Upload,
  Users,
  X,
} from "lucide-react";
import { useTaxonomy } from "@/features/home/api";
import { useMe } from "@/features/auth/api";
import { useTeamMembers } from "@/features/teams/api";
import {
  importStudents,
  studentsExportUrl,
  gradebookExportUrl,
  useClass,
  useCreateStudent,
  useDeleteClass,
  useDeleteStudent,
  useGradebook,
  useMyClasses,
  useStudents,
  useUpdateClass,
  useUpdateStudent,
} from "@/features/classes/api";
import { ClassAssignmentsPanel } from "@/features/classes/components/assignment-panel";
import type {
  ClassTeacherItem,
  StudentDto,
  StudentImportResult,
} from "@/features/classes/types";
import { EmptyState } from "@/components/common/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { formatDate, formatScore10 } from "@/lib/date";

type TabId = "hoc-sinh" | "bai-dao" | "bang-diem";

/** /gv/lop/:id — tabs: Học sinh · Bài đã giao · Bảng điểm (spec §5.2, §7). */
export function GvLopIdPage() {
  const navigate = useNavigate();
  const { id: idParam } = useParams();
  const id = Number(idParam);
  const validId = Number.isInteger(id) && id > 0 ? id : null;
  const [searchParams, setSearchParams] = useSearchParams();
  const rawTab = searchParams.get("tab") ?? "hoc-sinh";
  const tab: TabId =
    rawTab === "bai-dao" || rawTab === "bang-diem" ? rawTab : "hoc-sinh";

  const { data: cls, isPending, isError, error } = useClass(validId);
  // ClassDto chi tiết không có studentCount → lấy từ danh sách lớp.
  const { data: classesList } = useMyClasses();
  const del = useDeleteClass();
  const [editOpen, setEditOpen] = useState(false);

  const setTab = (t: TabId) => {
    if (t === "hoc-sinh") searchParams.delete("tab");
    else searchParams.set("tab", t);
    setSearchParams(searchParams, { replace: true });
  };

  if (!validId)
    return (
      <EmptyState
        title="Không hợp lệ"
        description="Đường dẫn lớp không đúng."
      />
    );
  if (isPending)
    return (
      <div className="space-y-3">
        <Skeleton className="h-9 w-64" />
        <Skeleton className="h-10 w-96" />
        <Skeleton className="h-96 w-full" />
      </div>
    );
  if (isError || !cls)
    return (
      <EmptyState
        title="Không tải được lớp"
        description={error instanceof Error ? error.message : undefined}
      />
    );

  const studentCount = classesList?.find((c) => c.id === cls.id)?.studentCount;

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <button
          type="button"
          aria-label="Quay lại danh sách lớp"
          onClick={() => navigate("/gv/lop")}
          className="rounded-btn border border-grid bg-white p-2 text-muted hover:border-violet hover:text-violet"
        >
          <ArrowLeft className="size-4" aria-hidden />
        </button>
        <h1 className="text-xl font-semibold">{cls.name}</h1>
        {cls.gradeName ? <Badge>{cls.gradeName}</Badge> : null}
        {cls.schoolYearName ? (
          <Badge variant="outline">{cls.schoolYearName}</Badge>
        ) : null}
        {cls.teamName ? (
          <Badge variant="outline">Tổ: {cls.teamName}</Badge>
        ) : null}
        <span className="text-xs text-muted">
          Chủ nhiệm: {cls.homeroomTeacherName ?? "—"}
        </span>
        <div className="ml-auto flex gap-2">
          <Button variant="outline" size="sm" onClick={() => setEditOpen(true)}>
            <Pencil className="size-3.5" aria-hidden /> Sửa thông tin
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="text-redpen hover:text-redpen"
            disabled={del.isPending}
            onClick={() => {
              if (
                confirm(
                  `Xóa lớp "${cls.name}"? Danh sách học sinh, bài đã giao và toàn bộ lượt làm sẽ bị xóa vĩnh viễn.`,
                )
              )
                void del.mutateAsync(cls.id).then(() => navigate("/gv/lop"));
            }}
          >
            <Trash2 className="size-3.5" aria-hidden /> Xóa lớp
          </Button>
        </div>
      </div>

      <div
        className="mb-4 flex flex-wrap gap-1 border-b border-grid"
        role="tablist"
        aria-label="Các tab của lớp"
      >
        {(
          [
            ["hoc-sinh", "Học sinh"],
            ["bai-dao", "Bài đã giao"],
            ["bang-diem", "Bảng điểm"],
          ] as [TabId, string][]
        ).map(([t, label]) => (
          <button
            key={t}
            type="button"
            role="tab"
            aria-selected={tab === t}
            onClick={() => setTab(t)}
            className={cn(
              "-mb-px border-b-2 px-3 py-2 text-sm transition",
              tab === t
                ? "border-violet font-medium text-violet"
                : "border-transparent text-muted hover:text-ink",
            )}
          >
            {label}
            {t === "hoc-sinh" && studentCount != null ? (
              <span className="ml-1 text-xs text-muted">
                ({studentCount.toLocaleString("vi-VN")})
              </span>
            ) : null}
          </button>
        ))}
      </div>

      {tab === "hoc-sinh" ? <StudentsTab classId={cls.id} /> : null}
      {tab === "bai-dao" ? <ClassAssignmentsPanel classId={cls.id} /> : null}
      {tab === "bang-diem" ? <GradebookTab classId={cls.id} /> : null}

      {editOpen ? (
        <EditClassModal classId={cls.id} onClose={() => setEditOpen(false)} />
      ) : null}
    </div>
  );
}

// ===== Tab Học sinh (spec §7) =====

function StudentsTab({ classId }: { classId: number }) {
  const { data: students, isPending } = useStudents(classId);
  const create = useCreateStudent(classId);
  const del = useDeleteStudent(classId);
  const fileRef = useRef<HTMLInputElement>(null);

  const [preview, setPreview] = useState<{
    file: File;
    result: StudentImportResult;
  } | null>(null);
  const [importing, setImporting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [modal, setModal] = useState<
    { mode: "create" } | { mode: "edit"; student: StudentDto } | null
  >(null);

  const runDryRun = async (file: File) => {
    setNotice(null);
    setImporting(true);
    try {
      const result = await importStudents(classId, file, true);
      setPreview({ file, result });
    } catch (e) {
      setNotice(
        e instanceof Error ? e.message : "Nhập file danh sách thất bại",
      );
    } finally {
      setImporting(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  };

  const commitImport = async () => {
    if (!preview) return;
    setImporting(true);
    try {
      const result = await importStudents(classId, preview.file, false);
      setPreview(null);
      setNotice(
        `Đã nhập ${result.importCount} học sinh` +
          (result.duplicateCount > 0
            ? `, bỏ qua ${result.duplicateCount} trùng.`
            : "."),
      );
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "Nhập danh sách thất bại");
    } finally {
      setImporting(false);
    }
  };

  return (
    <div className="space-y-4">
      <input
        ref={fileRef}
        type="file"
        accept=".xlsx"
        className="hidden"
        aria-hidden
        tabIndex={-1}
        onChange={(e) => {
          const f = e.target.files?.[0];
          if (f) void runDryRun(f);
        }}
      />
      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          disabled={importing}
          onClick={() => fileRef.current?.click()}
        >
          <Upload className="size-4" aria-hidden />
          {importing ? "Đang xử lý…" : "Nhập từ Excel"}
        </Button>
        <a
          href={studentsExportUrl(classId)}
          download
          className="inline-flex h-9 items-center gap-1.5 rounded-btn border border-grid bg-white px-3 text-sm text-ink transition hover:border-violet/40 hover:text-violet"
        >
          <Download className="size-4" aria-hidden /> Xuất Excel
        </a>
        <span className="flex items-center gap-1 text-xs text-muted">
          <a
            href="/templates/mau-danh-sach-hs.xlsx"
            download
            className="inline-flex items-center gap-1 hover:text-violet"
          >
            <Download className="size-3.5" aria-hidden /> Mẫu danh sách
          </a>
        </span>
        <Button
          className="ml-auto"
          onClick={() => setModal({ mode: "create" })}
          disabled={create.isPending}
        >
          <Plus className="size-4" aria-hidden /> Thêm học sinh
        </Button>
      </div>

      {notice ? (
        <p
          className={cn(
            "rounded-card border p-2 text-sm",
            notice.startsWith("Đã nhập")
              ? "border-correct/40 bg-correct/10 text-correct"
              : "border-redpen/30 bg-redpen/5 text-redpen",
          )}
          role="alert"
        >
          {notice}
        </p>
      ) : null}

      {preview ? (
        <ImportPreview
          preview={preview}
          busy={importing}
          onConfirm={() => void commitImport()}
          onCancel={() => setPreview(null)}
        />
      ) : null}

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-11" />
          ))}
        </div>
      ) : (students ?? []).length === 0 ? (
        <EmptyState
          icon={Users}
          title="Chưa có học sinh"
          description="Nhập từ file Excel mẫu hoặc thêm thủ công."
        />
      ) : (
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[760px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="p-2 text-right">STT</th>
                <th className="p-2">Họ tên</th>
                <th className="p-2">Mã HS</th>
                <th className="p-2">Ngày sinh</th>
                <th className="p-2">Giới tính</th>
                <th className="p-2">Trạng thái</th>
                <th className="p-2 text-right">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {(students ?? []).map((s) => (
                <tr
                  key={s.id}
                  className={cn(
                    "border-b border-grid/60 last:border-0 hover:bg-paper/50",
                    !s.isActive && "opacity-60",
                  )}
                >
                  <td className="p-2 text-right tabular-nums text-muted">
                    {s.ordinal ?? "—"}
                  </td>
                  <td className="p-2 font-medium">{s.fullName}</td>
                  <td className="p-2 text-muted">{s.studentCode ?? "—"}</td>
                  <td className="p-2 text-muted">
                    {s.dateOfBirth ? formatDate(s.dateOfBirth) : "—"}
                  </td>
                  <td className="p-2 text-muted">{s.gender ?? "—"}</td>
                  <td className="p-2">
                    {s.isActive ? (
                      <Badge className="bg-correct/10 text-correct">
                        Đang học
                      </Badge>
                    ) : (
                      <Badge className="bg-muted/10 text-muted">
                        Ngưng hoạt động
                      </Badge>
                    )}
                  </td>
                  <td className="p-2">
                    <div className="flex justify-end gap-1">
                      <Button
                        size="sm"
                        variant="ghost"
                        title="Sửa"
                        aria-label={`Sửa ${s.fullName}`}
                        onClick={() => setModal({ mode: "edit", student: s })}
                      >
                        <Pencil className="size-3.5" aria-hidden />
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        className="text-redpen hover:text-redpen"
                        title="Xóa"
                        aria-label={`Xóa ${s.fullName}`}
                        disabled={del.isPending}
                        onClick={() => {
                          if (
                            confirm(
                              `Xóa học sinh "${s.fullName}"? Các lượt làm bài liên quan sẽ bị xóa vĩnh viễn.`,
                            )
                          )
                            void del.mutateAsync(s.id);
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

      {modal ? (
        <StudentModal
          classId={classId}
          initial={modal.mode === "edit" ? modal.student : null}
          onClose={() => setModal(null)}
        />
      ) : null}
    </div>
  );
}

/** Xem trước kết quả dryRun trước khi ghi (spec §7: báo lỗi từng dòng). */
function ImportPreview({
  preview,
  busy,
  onConfirm,
  onCancel,
}: {
  preview: { file: File; result: StudentImportResult };
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const { result } = preview;
  return (
    <div className="rounded-card border border-violet/30 bg-violet/5 p-3">
      <div className="mb-2 flex flex-wrap items-center gap-2 text-sm">
        <span className="font-medium">Xem trước: {preview.file.name}</span>
        <Badge>{result.totalRows} dòng</Badge>
        <Badge className="bg-correct/10 text-correct">
          {result.importCount} sẽ nhập
        </Badge>
        {result.duplicateCount > 0 ? (
          <Badge className="bg-warn/10 text-warn">
            {result.duplicateCount} trùng
          </Badge>
        ) : null}
        {result.errors.length > 0 ? (
          <Badge className="bg-redpen/10 text-redpen">
            {result.errors.length} lỗi
          </Badge>
        ) : null}
        <span className="ml-auto flex gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={onCancel}
            disabled={busy}
          >
            Hủy
          </Button>
          <Button
            size="sm"
            onClick={onConfirm}
            disabled={busy || result.importCount === 0}
          >
            {busy ? "Đang nhập…" : `Xác nhận nhập (${result.importCount})`}
          </Button>
        </span>
      </div>
      {result.errors.length > 0 ? (
        <table className="w-full text-xs">
          <tbody>
            {result.errors.map((e) => (
              <tr key={e.row} className="border-t border-violet/10">
                <td className="py-1 pr-2 tabular-nums text-muted">
                  Dòng {e.row}
                </td>
                <td className="py-1 text-redpen">{e.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}

/** Modal thêm/sửa học sinh. dateOfBirth = input date (yyyy-MM-dd). */
function StudentModal({
  classId,
  initial,
  onClose,
}: {
  classId: number;
  initial: StudentDto | null;
  onClose: () => void;
}) {
  const create = useCreateStudent(classId);
  const update = useUpdateStudent();
  const [fullName, setFullName] = useState(initial?.fullName ?? "");
  const [studentCode, setStudentCode] = useState(initial?.studentCode ?? "");
  const [dob, setDob] = useState(initial?.dateOfBirth ?? "");
  const [gender, setGender] = useState(initial?.gender ?? "");
  const [ordinal, setOrdinal] = useState(initial?.ordinal ?? 0);
  const [isActive, setIsActive] = useState(initial?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);

  const save = async () => {
    setError(null);
    const body = {
      fullName: fullName.trim(),
      studentCode: studentCode.trim() || null,
      dateOfBirth: dob || null,
      gender: gender || null,
    };
    try {
      if (initial) {
        await update.mutateAsync({
          classId,
          studentId: initial.id,
          body: {
            ...body,
            ordinal: ordinal > 0 ? ordinal : null,
            isActive,
          },
        });
      } else {
        await create.mutateAsync(body);
      }
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không lưu được học sinh");
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-ink/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={initial ? "Sửa học sinh" : "Thêm học sinh"}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="w-full max-w-md rounded-card border border-grid bg-white p-4">
        <h3 className="mb-3 font-semibold">
          {initial ? "Sửa học sinh" : "Thêm học sinh"}
        </h3>
        <div className="space-y-3">
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Họ và tên *
            </span>
            <input
              type="text"
              value={fullName}
              maxLength={100}
              onChange={(e) => setFullName(e.target.value)}
              className="h-9 w-full rounded-btn border border-grid px-2.5 text-sm outline-none focus:border-violet"
            />
          </label>
          <div className="grid grid-cols-2 gap-3">
            <label className="block">
              <span className="mb-1 block text-xs font-medium text-muted">
                Mã HS
              </span>
              <input
                type="text"
                value={studentCode}
                maxLength={40}
                onChange={(e) => setStudentCode(e.target.value)}
                className="h-9 w-full rounded-btn border border-grid px-2.5 text-sm outline-none focus:border-violet"
              />
            </label>
            <label className="block">
              <span className="mb-1 block text-xs font-medium text-muted">
                Ngày sinh
              </span>
              <input
                type="date"
                value={dob}
                onChange={(e) => setDob(e.target.value)}
                className="h-9 w-full rounded-btn border border-grid bg-white px-2.5 text-sm outline-none focus:border-violet"
              />
            </label>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <label className="block">
              <span className="mb-1 block text-xs font-medium text-muted">
                Giới tính
              </span>
              <select
                value={gender}
                onChange={(e) => setGender(e.target.value)}
                className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
              >
                <option value="">—</option>
                <option value="Nam">Nam</option>
                <option value="Nữ">Nữ</option>
              </select>
            </label>
            {initial ? (
              <label className="block">
                <span className="mb-1 block text-xs font-medium text-muted">
                  Thứ tự (STT)
                </span>
                <input
                  type="number"
                  min={0}
                  value={ordinal || ""}
                  onChange={(e) => setOrdinal(Number(e.target.value) || 0)}
                  className="h-9 w-full rounded-btn border border-grid px-2.5 text-sm outline-none focus:border-violet"
                />
              </label>
            ) : null}
          </div>
          {initial ? (
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                className="size-4 accent-violet"
              />
              Đang hoạt động
            </label>
          ) : null}
          {error ? (
            <p className="text-sm text-redpen" role="alert">
              {error}
            </p>
          ) : null}
          <div className="flex justify-end gap-2">
            <Button variant="outline" onClick={onClose}>
              Hủy
            </Button>
            <Button
              onClick={() => void save()}
              disabled={
                create.isPending ||
                update.isPending ||
                fullName.trim().length === 0
              }
            >
              {create.isPending || update.isPending ? "Đang lưu…" : "Lưu"}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}

// ===== Tab Bảng điểm (spec §7) =====

function GradebookTab({ classId }: { classId: number }) {
  const { data, isPending } = useGradebook(classId);

  if (isPending) return <Skeleton className="h-96 w-full" />;
  if (!data) return <EmptyState title="Không tải được bảng điểm" />;

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <span className="text-sm text-muted">
          Ô trống = chưa làm · điểm = lượt cao nhất (thang 10)
        </span>
        <a
          href={gradebookExportUrl(classId)}
          download
          className="ml-auto inline-flex h-9 items-center gap-1.5 rounded-btn border border-grid bg-white px-3 text-sm text-ink transition hover:border-violet/40 hover:text-violet"
        >
          <Download className="size-4" aria-hidden /> Xuất Excel
        </a>
      </div>
      {data.columns.length === 0 || data.students.length === 0 ? (
        <EmptyState
          title="Chưa có dữ liệu"
          description="Giao bài tập và chờ học sinh nộp để có bảng điểm."
        />
      ) : (
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="sticky left-0 bg-paper p-2">Học sinh</th>
                {data.columns.map((c) => (
                  <th key={c.assignmentId} className="min-w-28 p-2">
                    <span className="line-clamp-2" title={c.quizTitle}>
                      {c.quizTitle}
                    </span>
                    <span className="block font-mono text-[11px] tracking-widest">
                      {c.code}
                    </span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.students.map((s, rowIdx) => (
                <tr
                  key={s.studentId}
                  className="border-b border-grid/60 last:border-0"
                >
                  <td className="sticky left-0 max-w-56 truncate bg-white p-2 font-medium">
                    {s.fullName}
                  </td>
                  {data.columns.map((c, colIdx) => {
                    const score = data.scores[rowIdx]?.[colIdx] ?? null;
                    return (
                      <td
                        key={c.assignmentId}
                        className={cn(
                          "p-2 text-right tabular-nums",
                          score == null && "bg-warn/10 text-warn",
                        )}
                        title={score == null ? "Chưa làm" : undefined}
                      >
                        {formatScore10(score)}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ===== Modal sửa thông tin lớp =====

function EditClassModal({
  classId,
  onClose,
}: {
  classId: number;
  onClose: () => void;
}) {
  const { data: cls } = useClass(classId);
  const { data: tax } = useTaxonomy();
  const { data: me } = useMe();
  const update = useUpdateClass();
  const [name, setName] = useState(cls?.name ?? "");
  const [gradeId, setGradeId] = useState<number>(cls?.gradeId ?? 0);
  const [schoolYearId, setSchoolYearId] = useState<number>(
    cls?.schoolYearId ?? 0,
  );
  const [teamId, setTeamId] = useState<number>(cls?.teamId ?? 0);
  const [teachers, setTeachers] = useState<ClassTeacherItem[]>(
    cls?.teachers.map((t) => ({
      userId: t.userId,
      subjectId: t.subjectId,
    })) ?? [],
  );
  const [error, setError] = useState<string | null>(null);

  const myTeams = (me?.teams ?? []).map((t) => ({ id: t.id, name: t.name }));
  const membersTeamId = teamId || cls?.teamId;
  const { data: members } = useTeamMembers(
    membersTeamId != null ? membersTeamId : undefined,
  );

  const save = async () => {
    setError(null);
    try {
      await update.mutateAsync({
        id: classId,
        body: {
          name: name.trim() || null,
          gradeId: gradeId || null,
          schoolYearId: schoolYearId || null,
          teamId: teamId || null,
          teachers: teachers.filter((t) => t.userId !== 0),
        },
      });
      onClose();
    } catch (e) {
      const fields = (e as { data?: { errors?: Record<string, string[]> } })
        ?.data?.errors;
      setError(
        fields
          ? Object.values(fields).flat().join(" ")
          : e instanceof Error
            ? e.message
            : "Không lưu được lớp",
      );
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-ink/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label="Sửa thông tin lớp"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-card border border-grid bg-white p-4">
        <div className="mb-3 flex items-center justify-between">
          <h3 className="font-semibold">Sửa thông tin lớp</h3>
          <Button variant="ghost" size="sm" aria-label="Đóng" onClick={onClose}>
            <X className="size-4" aria-hidden />
          </Button>
        </div>
        <div className="space-y-3">
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Tên lớp *
            </span>
            <input
              type="text"
              value={name}
              maxLength={30}
              onChange={(e) => setName(e.target.value)}
              className="h-9 w-full rounded-btn border border-grid px-2.5 text-sm outline-none focus:border-violet"
            />
          </label>
          <div className="grid grid-cols-2 gap-3">
            <label className="block">
              <span className="mb-1 block text-xs font-medium text-muted">
                Khối
              </span>
              <select
                value={gradeId}
                onChange={(e) => setGradeId(Number(e.target.value))}
                className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
              >
                <option value={0}>—</option>
                {(tax?.grades ?? []).map((g) => (
                  <option key={g.id} value={g.id}>
                    Khối {g.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="block">
              <span className="mb-1 block text-xs font-medium text-muted">
                Năm học
              </span>
              <select
                value={schoolYearId}
                onChange={(e) => setSchoolYearId(Number(e.target.value))}
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
            </label>
          </div>
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Tổ
            </span>
            <select
              value={teamId}
              onChange={(e) => {
                setTeamId(Number(e.target.value));
                setTeachers([]);
              }}
              className="h-9 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            >
              <option value={0}>—</option>
              {myTeams.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
          </label>

          <div>
            <span className="mb-1 block text-xs font-medium text-muted">
              GV bộ môn
            </span>
            {membersTeamId == null ? (
              <p className="text-xs text-muted">Chọn tổ để thêm GV bộ môn.</p>
            ) : (
              <div className="space-y-2">
                {teachers.map((t, i) => (
                  <div key={i} className="flex items-center gap-2">
                    <select
                      aria-label={`GV bộ môn ${i + 1}`}
                      value={t.userId}
                      onChange={(e) =>
                        setTeachers((ts) =>
                          ts.map((x, j) =>
                            j === i
                              ? { ...x, userId: Number(e.target.value) }
                              : x,
                          ),
                        )
                      }
                      className="h-9 min-w-0 flex-1 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                    >
                      <option value={0}>Chọn GV…</option>
                      {(members ?? []).map((m) => (
                        <option key={m.userId} value={m.userId}>
                          {m.fullName}
                        </option>
                      ))}
                    </select>
                    <select
                      aria-label={`Môn của GV bộ môn ${i + 1}`}
                      value={t.subjectId ?? ""}
                      onChange={(e) =>
                        setTeachers((ts) =>
                          ts.map((x, j) =>
                            j === i
                              ? {
                                  ...x,
                                  subjectId:
                                    e.target.value === ""
                                      ? null
                                      : Number(e.target.value),
                                }
                              : x,
                          ),
                        )
                      }
                      className="h-9 w-36 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                    >
                      <option value="">Môn…</option>
                      {(tax?.subjects ?? []).map((s) => (
                        <option key={s.id} value={s.id}>
                          {s.name}
                        </option>
                      ))}
                    </select>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      aria-label={`Bỏ GV bộ môn ${i + 1}`}
                      onClick={() =>
                        setTeachers((ts) => ts.filter((_, j) => j !== i))
                      }
                    >
                      <X className="size-3.5" aria-hidden />
                    </Button>
                  </div>
                ))}
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    setTeachers((ts) => [...ts, { userId: 0, subjectId: null }])
                  }
                >
                  <Plus className="size-3.5" aria-hidden /> Thêm GV bộ môn
                </Button>
              </div>
            )}
          </div>

          {error ? (
            <p className="text-sm text-redpen" role="alert">
              {error}
            </p>
          ) : null}
          <div className="flex items-center justify-end gap-2">
            <Button variant="outline" onClick={onClose}>
              Hủy
            </Button>
            <Button
              onClick={() => void save()}
              disabled={update.isPending || name.trim().length === 0}
            >
              {update.isPending ? "Đang lưu…" : "Lưu"}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}

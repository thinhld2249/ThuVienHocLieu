import { useState } from "react";
import { useNavigate } from "react-router";
import { GraduationCap, Plus, Trash2, X } from "lucide-react";
import { useTaxonomy } from "@/features/home/api";
import { useMe } from "@/features/auth/api";
import { useTeamMembers } from "@/features/teams/api";
import {
  useCreateClass,
  useDeleteClass,
  useMyClasses,
} from "@/features/classes/api";
import type { ClassTeacherItem } from "@/features/classes/types";
import { EmptyState } from "@/components/common/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";

/** /gv/lop — danh sách lớp của tôi (spec §5.2, §7). */
export function GvLopPage() {
  const navigate = useNavigate();
  const { data: me } = useMe();
  const del = useDeleteClass();
  const [createOpen, setCreateOpen] = useState(false);
  const myTeams = (me?.teams ?? []).map((t) => ({ id: t.id, name: t.name }));

  const { data: classes, isPending } = useMyClasses();

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Lớp</h1>
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="size-4" aria-hidden /> Tạo lớp
        </Button>
      </div>

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : (classes ?? []).length === 0 ? (
        <EmptyState
          icon={GraduationCap}
          title="Chưa có lớp nào"
          description="Tạo lớp để nhập danh sách học sinh và giao bài tập bằng mã/QR."
          action={
            <Button
              size="sm"
              variant="secondary"
              onClick={() => setCreateOpen(true)}
            >
              <Plus className="size-4" aria-hidden /> Tạo lớp đầu tiên
            </Button>
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[820px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="p-2">Tên lớp</th>
                <th className="p-2">Khối</th>
                <th className="p-2">Năm học</th>
                <th className="p-2">Tổ</th>
                <th className="p-2 text-right">Học sinh</th>
                <th className="p-2 text-right">Bài đã giao</th>
                <th className="p-2">GV bộ môn</th>
                <th className="p-2">Tạo lúc</th>
                <th className="p-2 text-right">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {(classes ?? []).map((c) => (
                <tr
                  key={c.id}
                  className="border-b border-grid/60 last:border-0 hover:bg-paper/50"
                >
                  <td className="p-2">
                    <button
                      type="button"
                      onClick={() => navigate(`/gv/lop/${c.id}`)}
                      className="font-medium hover:text-violet"
                    >
                      {c.name}
                    </button>
                    {c.isArchived ? (
                      <Badge className="ml-1.5 bg-muted/10 text-muted">
                        Đã lưu trữ
                      </Badge>
                    ) : null}
                  </td>
                  <td className="p-2 text-muted">
                    {c.gradeId != null
                      ? (c.gradeName ?? `Khối ${c.gradeId}`)
                      : "—"}
                  </td>
                  <td className="p-2 text-muted">{c.schoolYearName ?? "—"}</td>
                  <td className="p-2 text-muted">{c.teamName ?? "—"}</td>
                  <td className="p-2 text-right tabular-nums">
                    {c.studentCount.toLocaleString("vi-VN")}
                  </td>
                  <td className="p-2 text-right tabular-nums text-muted">
                    {c.assignmentCount.toLocaleString("vi-VN")}
                  </td>
                  <td className="max-w-48 p-2 text-muted">
                    <span className="line-clamp-2">
                      {c.teachers.length > 0
                        ? c.teachers.map((t) => t.fullName).join(", ")
                        : "—"}
                    </span>
                  </td>
                  <td className="p-2 text-muted">
                    {formatDateTime(c.createdAt)}
                  </td>
                  <td className="p-2">
                    <div className="flex justify-end gap-1">
                      <Button
                        size="sm"
                        variant="ghost"
                        className="text-redpen hover:text-redpen"
                        title="Xóa"
                        aria-label={`Xóa lớp ${c.name}`}
                        disabled={del.isPending}
                        onClick={() => {
                          if (
                            confirm(
                              `Xóa lớp "${c.name}"? Danh sách học sinh, bài đã giao và toàn bộ lượt làm của lớp sẽ bị xóa vĩnh viễn.`,
                            )
                          )
                            void del.mutateAsync(c.id);
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

      {createOpen ? (
        <CreateClassModal
          myTeams={myTeams}
          onClose={() => setCreateOpen(false)}
          onCreated={(id) => navigate(`/gv/lop/${id}`)}
        />
      ) : null}
    </div>
  );
}

/** Modal tạo lớp: tên · khối · năm học · tổ · GV bộ môn (tùy chọn). */
function CreateClassModal({
  myTeams,
  onClose,
  onCreated,
}: {
  myTeams: { id: number; name: string }[];
  onClose: () => void;
  onCreated: (id: number) => void;
}) {
  const { data: tax } = useTaxonomy();
  const create = useCreateClass();
  const [name, setName] = useState("");
  const [gradeId, setGradeId] = useState<number | "">("");
  const [schoolYearId, setSchoolYearId] = useState<number | "">("");
  const [teamId, setTeamId] = useState<number | "">("");
  const [teachers, setTeachers] = useState<ClassTeacherItem[]>([]);
  const [error, setError] = useState<string | null>(null);

  const currentYear = (tax?.schoolYears ?? []).find((y) => y.isCurrent);
  const selectedTeamId = teamId === "" ? null : teamId;
  const { data: members } = useTeamMembers(selectedTeamId ?? undefined);

  const setTeacher = (index: number, patch: Partial<ClassTeacherItem>) => {
    setTeachers((ts) =>
      ts.map((t, i) => (i === index ? { ...t, ...patch } : t)),
    );
  };

  const submit = async () => {
    setError(null);
    const validTeachers = teachers.filter((t) => t.userId !== 0);
    try {
      const cls = await create.mutateAsync({
        name: name.trim(),
        gradeId: gradeId === "" ? null : gradeId,
        schoolYearId: schoolYearId === "" ? null : schoolYearId,
        teamId: teamId === "" ? null : teamId,
        teachers: validTeachers.length > 0 ? validTeachers : null,
      });
      onCreated(cls.id);
    } catch (e) {
      const fields = (e as { data?: { errors?: Record<string, string[]> } })
        ?.data?.errors;
      setError(
        fields
          ? Object.values(fields).flat().join(" ")
          : e instanceof Error
            ? e.message
            : "Không tạo được lớp",
      );
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label="Tạo lớp mới"
    >
      <form
        className="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-card bg-white p-5"
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <div className="mb-3 flex items-center justify-between">
          <h2 className="font-semibold">Tạo lớp mới</h2>
          <Button type="button" variant="ghost" size="sm" onClick={onClose}>
            <X className="size-4" aria-hidden />
          </Button>
        </div>
        <label className="mb-1 block text-sm font-medium">
          Tên lớp (bắt buộc)
        </label>
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={30}
          placeholder="VD: 5A"
          className="mb-3 h-10 w-full rounded-btn border border-grid px-3 text-sm outline-none focus:border-violet"
        />
        <div className="mb-3 grid gap-3 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-sm font-medium">Khối</span>
            <select
              value={gradeId}
              onChange={(e) =>
                setGradeId(e.target.value === "" ? "" : Number(e.target.value))
              }
              className="h-10 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            >
              <option value="">Chọn khối…</option>
              {(tax?.grades ?? []).map((g) => (
                <option key={g.id} value={g.id}>
                  Khối {g.name}
                </option>
              ))}
            </select>
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium">Năm học</span>
            <select
              value={schoolYearId}
              onChange={(e) =>
                setSchoolYearId(
                  e.target.value === "" ? "" : Number(e.target.value),
                )
              }
              className="h-10 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            >
              <option value="">
                {currentYear ? currentYear.name : "Năm học hiện tại"}
              </option>
              {(tax?.schoolYears ?? [])
                .filter((y) => y.id !== currentYear?.id)
                .map((y) => (
                  <option key={y.id} value={y.id}>
                    {y.name}
                  </option>
                ))}
            </select>
          </label>
        </div>
        <label className="mb-1 block text-sm font-medium">
          Tổ (trống = tổ đầu tiên của bạn)
        </label>
        <select
          value={teamId}
          onChange={(e) => {
            setTeamId(e.target.value === "" ? "" : Number(e.target.value));
            setTeachers([]);
          }}
          className="mb-3 h-10 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
        >
          <option value="">—</option>
          {myTeams.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </select>

        <div className="mb-1 text-sm font-medium">GV bộ môn (tùy chọn)</div>
        <p className="mb-2 text-xs text-muted">
          Chọn từ thành viên tổ — GV đó cũng có quyền quản lý lớp.
        </p>
        {selectedTeamId == null ? (
          <p className="mb-3 text-xs text-muted">
            Chọn tổ ở trên để chọn GV bộ môn.
          </p>
        ) : (members ?? []).length === 0 ? (
          <p className="mb-3 text-xs text-muted">
            Tổ chưa có thành viên khác để phân công.
          </p>
        ) : (
          <div className="mb-3 space-y-2">
            {teachers.map((t, i) => (
              <div key={i} className="flex items-center gap-2">
                <select
                  aria-label={`GV bộ môn ${i + 1}`}
                  value={t.userId}
                  onChange={(e) =>
                    setTeacher(i, { userId: Number(e.target.value) })
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
                    setTeacher(i, {
                      subjectId:
                        e.target.value === "" ? null : Number(e.target.value),
                    })
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
              disabled={(members ?? []).length === 0}
            >
              <Plus className="size-3.5" aria-hidden /> Thêm GV bộ môn
            </Button>
          </div>
        )}

        {error ? (
          <p className="mb-3 text-sm text-redpen" role="alert">
            {error}
          </p>
        ) : null}
        <p className="mb-4 text-xs text-muted">
          Bạn sẽ là GV chủ nhiệm của lớp. Tên lớp không trùng trong cùng năm
          học.
        </p>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Hủy
          </Button>
          <Button type="submit" disabled={create.isPending}>
            {create.isPending ? "Đang tạo…" : "Tạo lớp"}
          </Button>
        </div>
      </form>
    </div>
  );
}

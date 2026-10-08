import { useState } from "react";
import { GraduationCap } from "lucide-react";
import {
  useAdminChangeHomeroom,
  useAdminClasses,
  useAdminGrades,
  useAdminSchoolYears,
  useAdminUsersList,
} from "@/features/admin/api";
import type { AdminClass } from "@/features/admin/types";
import { EmptyState } from "@/components/common/EmptyState";
import { Pagination } from "@/components/common/Pagination";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";

/** /admin/lop — mọi lớp, đổi GV chủ nhiệm (spec §5.4). */
export function AdminClassesPage() {
  const [schoolYearId, setSchoolYearId] = useState(0);
  const [gradeId, setGradeId] = useState(0);
  const [q, setQ] = useState("");
  const [archived, setArchived] = useState(false);
  const [page, setPage] = useState(1);

  const { data: years } = useAdminSchoolYears();
  const { data: grades } = useAdminGrades();
  const { data: paged, isPending } = useAdminClasses({
    schoolYearId: schoolYearId || undefined,
    gradeId: gradeId || undefined,
    q: q || undefined,
    archived,
    page,
    pageSize: 24,
  });

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Lớp</h1>
        <div className="flex flex-wrap items-center gap-2">
          <select
            aria-label="Lọc theo năm học"
            value={schoolYearId}
            onChange={(e) => {
              setSchoolYearId(Number(e.target.value));
              setPage(1);
            }}
            className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value={0}>Mọi năm học</option>
            {(years ?? []).map((y) => (
              <option key={y.id} value={y.id}>
                {y.name}
              </option>
            ))}
          </select>
          <select
            aria-label="Lọc theo khối"
            value={gradeId}
            onChange={(e) => {
              setGradeId(Number(e.target.value));
              setPage(1);
            }}
            className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value={0}>Mọi khối</option>
            {(grades ?? []).map((g) => (
              <option key={g.id} value={g.id}>
                Khối {g.name}
              </option>
            ))}
          </select>
          <input
            type="search"
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
              setPage(1);
            }}
            placeholder="Tìm tên lớp…"
            className="h-10 w-44 rounded-btn border border-grid bg-white px-3 text-sm outline-none focus:border-violet"
          />
          <label className="flex items-center gap-1.5 text-sm text-muted">
            <input
              type="checkbox"
              checked={archived}
              onChange={(e) => {
                setArchived(e.target.checked);
                setPage(1);
              }}
              className="size-4 accent-violet"
            />
            Đã lưu trữ
          </label>
        </div>
      </div>

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : (paged?.items ?? []).length === 0 ? (
        <EmptyState
          icon={GraduationCap}
          title="Không có lớp phù hợp"
          description="Thử đổi bộ lọc năm học, khối hoặc từ khóa."
        />
      ) : (
        <>
          <div className="overflow-x-auto rounded-card border border-grid bg-white">
            <table className="w-full min-w-[900px] text-sm">
              <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
                <tr>
                  <th className="p-2">Lớp</th>
                  <th className="p-2">Khối</th>
                  <th className="p-2">Năm học</th>
                  <th className="p-2">Tổ</th>
                  <th className="p-2">GV chủ nhiệm</th>
                  <th className="p-2 text-right">Học sinh</th>
                  <th className="p-2">Tạo lúc</th>
                </tr>
              </thead>
              <tbody>
                {(paged?.items ?? []).map((c) => (
                  <ClassRow key={c.id} cls={c} />
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={paged?.page ?? 1}
            pageSize={24}
            total={paged?.total ?? 0}
            onChange={setPage}
          />
        </>
      )}
    </div>
  );
}

function ClassRow({ cls }: { cls: AdminClass }) {
  const change = useAdminChangeHomeroom();
  const [teacherId, setTeacherId] = useState(cls.homeroomTeacherId ?? 0);
  const { data: teachers } = useAdminUsersList({
    status: "Active",
    page: 1,
    pageSize: 100,
  });
  return (
    <tr className="border-b border-grid/60 last:border-0 hover:bg-paper/50">
      <td className="p-2">
        <span className="font-medium">{cls.name}</span>
        {cls.isArchived ? (
          <Badge className="ml-1.5 bg-muted/10 text-muted">Đã lưu trữ</Badge>
        ) : null}
      </td>
      <td className="p-2 text-muted">
        {cls.gradeId != null ? (cls.gradeName ?? `Khối ${cls.gradeId}`) : "—"}
      </td>
      <td className="p-2 text-muted">{cls.schoolYearName ?? "—"}</td>
      <td className="p-2 text-muted">{cls.teamName ?? "—"}</td>
      <td className="p-2">
        <div className="flex items-center gap-1.5">
          <select
            aria-label={`GV chủ nhiệm lớp ${cls.name}`}
            value={teacherId}
            onChange={(e) => setTeacherId(Number(e.target.value))}
            className="h-9 max-w-56 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value={0}>{cls.homeroomTeacherName ?? "Chưa có"}</option>
            {(teachers?.items ?? []).map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </select>
          <Button
            size="sm"
            variant="outline"
            disabled={
              change.isPending || teacherId === (cls.homeroomTeacherId ?? 0)
            }
            onClick={() =>
              void change.mutateAsync({
                classId: cls.id,
                homeroomTeacherId: teacherId,
              })
            }
          >
            Lưu
          </Button>
        </div>
      </td>
      <td className="p-2 text-right tabular-nums">
        {cls.activeStudentCount.toLocaleString("vi-VN")}
      </td>
      <td className="p-2 text-muted">{formatDateTime(cls.createdAt)}</td>
    </tr>
  );
}

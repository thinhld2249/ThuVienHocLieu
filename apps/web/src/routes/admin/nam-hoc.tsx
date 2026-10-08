import { useState } from "react";
import { CalendarRange, Plus } from "lucide-react";
import {
  useAdminCreateSchoolYear,
  useAdminRollover,
  useAdminSchoolYears,
} from "@/features/admin/api";
import type { RolloverResult } from "@/features/admin/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";

/** /admin/nam-hoc — danh sách năm học, tạo năm mới, kết chuyển năm học (spec §5.4). */
export function AdminYearsPage() {
  const { data: years, isPending } = useAdminSchoolYears();
  const createYear = useAdminCreateSchoolYear();
  const rollover = useAdminRollover();

  const [yearName, setYearName] = useState("");
  const [yearStart, setYearStart] = useState("2026-09-05");
  const [yearEnd, setYearEnd] = useState("2027-05-31");

  const [targetYearId, setTargetYearId] = useState(0);
  const [clone, setClone] = useState(true);
  const [result, setResult] = useState<RolloverResult | null>(null);

  const current = (years ?? []).find((y) => y.isCurrent);
  const targets = (years ?? []).filter((y) => !y.isCurrent);

  if (isPending) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} className="h-20" />
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <h1 className="text-xl font-semibold">Năm học</h1>

      <section className="rounded-card border border-grid bg-white p-4">
        <div className="mb-3 flex items-center gap-2">
          <CalendarRange className="size-4 text-violet" aria-hidden />
          <h2 className="text-sm font-semibold">Danh sách năm học</h2>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[520px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="p-2">Năm học</th>
                <th className="p-2">Bắt đầu</th>
                <th className="p-2">Kết thúc</th>
                <th className="p-2">Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              {(years ?? []).map((y) => (
                <tr
                  key={y.id}
                  className="border-b border-grid/60 last:border-0"
                >
                  <td className="p-2 font-medium">{y.name}</td>
                  <td className="p-2 text-muted">{y.startDate}</td>
                  <td className="p-2 text-muted">{y.endDate}</td>
                  <td className="p-2">
                    {y.isCurrent ? (
                      <Badge className="bg-correct/10 text-correct">
                        Năm hiện tại
                      </Badge>
                    ) : (
                      <span className="text-muted">Lưu trữ</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <form
          className="mt-3 flex flex-wrap items-end gap-2 border-t border-grid pt-3"
          onSubmit={(e) => {
            e.preventDefault();
            void createYear
              .mutateAsync({
                name: yearName.trim(),
                startDate: yearStart,
                endDate: yearEnd,
              })
              .then(() => setYearName(""));
          }}
        >
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Tên năm học
            </span>
            <input
              type="text"
              required
              value={yearName}
              onChange={(e) => setYearName(e.target.value)}
              placeholder="VD: 2027-2028"
              className="h-10 w-40 rounded-btn border border-grid bg-white px-3 text-sm outline-none focus:border-violet"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Bắt đầu
            </span>
            <input
              type="date"
              required
              value={yearStart}
              onChange={(e) => setYearStart(e.target.value)}
              className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-muted">
              Kết thúc
            </span>
            <input
              type="date"
              required
              value={yearEnd}
              onChange={(e) => setYearEnd(e.target.value)}
              className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
            />
          </label>
          <Button type="submit" disabled={createYear.isPending}>
            <Plus className="size-4" aria-hidden /> Tạo
          </Button>
        </form>
      </section>

      <section className="rounded-card border border-warn/40 bg-warn/5 p-4">
        <h2 className="mb-1 text-sm font-semibold">Kết chuyển năm học</h2>
        <p className="mb-3 text-sm text-muted">
          Chuyển sang năm học mới: năm hiện tại
          {current ? ` (${current.name})` : ""} bị lưu trữ, các lớp của năm cũ
          chuyển sang trạng thái đã lưu trữ. Việc kết chuyển không thể tự động
          hoàn tác.
        </p>
        <div className="flex flex-wrap items-center gap-3">
          <select
            aria-label="Năm học mới"
            value={targetYearId}
            onChange={(e) => setTargetYearId(Number(e.target.value))}
            className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value={0}>Chọn năm học mới…</option>
            {targets.map((y) => (
              <option key={y.id} value={y.id}>
                {y.name}
              </option>
            ))}
          </select>
          <label className="flex items-center gap-1.5 text-sm">
            <input
              type="checkbox"
              checked={clone}
              onChange={(e) => setClone(e.target.checked)}
              className="size-4 accent-violet"
            />
            Nhân bản lớp lên khối +1 (kèm học sinh &amp; GV bộ môn; khối 5 chỉ
            lưu trữ)
          </label>
          <Button
            disabled={targetYearId === 0 || rollover.isPending}
            onClick={() => {
              const target = targets.find((y) => y.id === targetYearId);
              if (
                !target ||
                !window.confirm(
                  `Kết chuyển năm học sang ${target.name}?\n\n` +
                    `- ${current?.name ?? "Năm hiện tại"} sẽ được lưu trữ.\n` +
                    `- Các lớp của năm cũ chuyển sang đã lưu trữ.` +
                    (clone
                      ? `\n- Lớp khối 1–4 được nhân bản lên khối +1 kèm học sinh.`
                      : ""),
                )
              )
                return;
              void rollover
                .mutateAsync({
                  yearId: targetYearId,
                  cloneClassesToNextGrade: clone,
                })
                .then(setResult)
                .catch(() => setResult(null));
            }}
          >
            {rollover.isPending
              ? "Đang kết chuyển…"
              : "Kết chuyển năm học"}
          </Button>
        </div>
        {result ? (
          <p className="mt-3 rounded-btn bg-correct/10 p-2 text-sm text-correct" role="status">
            Đã kết chuyển: lưu trữ {result.archivedClasses} lớp, nhân bản{" "}
            {result.clonedClasses} lớp ({result.clonedStudents} học sinh).
          </p>
        ) : null}
      </section>
    </div>
  );
}

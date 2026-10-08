import { useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router";
import dayjs from "dayjs";
import {
  CalendarClock,
  Copy,
  Eye,
  EyeOff,
  Pencil,
  Plus,
  RotateCcw,
  Search,
  Trash2,
} from "lucide-react";
import { useTaxonomy } from "@/features/home/api";
import {
  useBulkPublish,
  useDeleteDocument,
  useDuplicateDocument,
  useMyDocuments,
  usePublishContent,
  useRestoreDocument,
} from "@/features/documents/api";
import {
  PublishControl,
  schedulePresetValues,
  type SchedulePreset,
} from "@/components/common/PublishControl";
import { Pagination } from "@/components/common/Pagination";
import { EmptyState } from "@/components/common/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";
import { cn } from "@/lib/utils";

const PAGE_SIZE = 20;

/** Popover hẹn giờ cho thao tác hàng loạt (cùng preset với PublishControl). */
function BulkSchedulePopover({
  onDone,
  onClose,
}: {
  onDone: (from: string | null, until: string | null) => void;
  onClose: () => void;
}) {
  const [preset, setPreset] = useState<SchedulePreset>("weekend");
  const [from, setFrom] = useState(() => schedulePresetValues("weekend").from);
  const [until, setUntil] = useState(
    () => schedulePresetValues("weekend").until,
  );
  const popRef = useRef<HTMLDivElement>(null);

  return (
    <div
      ref={popRef}
      role="dialog"
      aria-label="Hẹn giờ hàng loạt"
      className="absolute left-0 top-full z-30 mt-2 w-72 rounded-card border border-grid bg-white p-3 shadow-lg"
    >
      <div className="mb-2 flex gap-1" role="group" aria-label="Preset">
        {(
          [
            ["weekend", "Cuối tuần này"],
            ["7days", "7 ngày"],
            ["custom", "Tùy chỉnh"],
          ] as [SchedulePreset, string][]
        ).map(([p, label]) => (
          <button
            key={p}
            type="button"
            onClick={() => {
              setPreset(p);
              if (p !== "custom") {
                const v = schedulePresetValues(p);
                setFrom(v.from);
                setUntil(v.until);
              }
            }}
            className={cn(
              "rounded-btn px-2 py-1 text-xs transition",
              preset === p
                ? "bg-violet text-white"
                : "bg-paper text-muted hover:text-violet",
            )}
          >
            {label}
          </button>
        ))}
      </div>
      <label className="mb-1 block text-xs text-muted">Từ</label>
      <input
        type="datetime-local"
        value={from}
        onChange={(e) => {
          setFrom(e.target.value);
          setPreset("custom");
        }}
        className="mb-2 h-9 w-full rounded-btn border border-grid px-2 text-sm outline-none focus:border-violet"
      />
      <label className="mb-1 block text-xs text-muted">
        Đến (trống = không giới hạn)
      </label>
      <input
        type="datetime-local"
        value={until}
        onChange={(e) => {
          setUntil(e.target.value);
          setPreset("custom");
        }}
        className="mb-3 h-9 w-full rounded-btn border border-grid px-2 text-sm outline-none focus:border-violet"
      />
      <div className="flex gap-2">
        <Button
          size="sm"
          className="flex-1"
          disabled={!from}
          onClick={() =>
            onDone(
              from ? dayjs(from).utc().toISOString() : null,
              until ? dayjs(until).utc().toISOString() : null,
            )
          }
        >
          Lên lịch
        </Button>
        <Button size="sm" variant="ghost" onClick={onClose}>
          Hủy
        </Button>
      </div>
    </div>
  );
}

/** /gv/tai-lieu — bảng tài liệu của tôi (spec §5.2). */
export function GvTaiLieuPage() {
  const navigate = useNavigate();
  const { data: taxonomy } = useTaxonomy();
  const [q, setQ] = useState("");
  const [section, setSection] = useState("");
  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [scheduleOpen, setScheduleOpen] = useState(false);

  const { data, isPending } = useMyDocuments({
    q: q.trim() || null,
    section: section || null,
    includeDeleted,
    page,
    pageSize: PAGE_SIZE,
  });
  const publish = usePublishContent();
  const bulk = useBulkPublish();
  const del = useDeleteDocument();
  const restore = useRestoreDocument();
  const duplicate = useDuplicateDocument();

  const rows = useMemo(() => data?.items ?? [], [data]);
  const activeRows = useMemo(() => rows.filter((r) => !r.isDeleted), [rows]);
  const allSelected =
    activeRows.length > 0 && activeRows.every((r) => selected.has(r.id));

  const toggleAll = () => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (allSelected) activeRows.forEach((r) => next.delete(r.id));
      else activeRows.forEach((r) => next.add(r.id));
      return next;
    });
  };
  const toggleOne = (id: number) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const runBulk = async (
    mode: "Visible" | "Hidden" | "Scheduled",
    from?: string | null,
    until?: string | null,
  ) => {
    const items = [...selected].map((id) => ({ kind: "document", id }));
    if (items.length === 0) return;
    try {
      const result = await bulk.mutateAsync({
        items,
        mode,
        from: from ?? null,
        until: until ?? null,
      });
      setSelected(new Set());
      setScheduleOpen(false);
      if (result.failed.length > 0)
        alert(
          `Đã áp dụng ${result.applied} mục, ${result.failed.length} mục thất bại.`,
        );
    } catch {
      setScheduleOpen(false);
    }
  };

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Tài liệu</h1>
        <Link to="/gv/tai-lieu/moi">
          <Button>
            <Plus className="size-4" aria-hidden /> Tài liệu mới
          </Button>
        </Link>
      </div>

      {/* Toolbar */}
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative">
          <Search
            className="absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted"
            aria-hidden
          />
          <input
            type="search"
            aria-label="Tìm tài liệu của tôi"
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
          {(taxonomy?.sections ?? []).map((s) => (
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

      {/* Bulk bar */}
      {selected.size > 0 ? (
        <div className="relative mb-3 flex flex-wrap items-center gap-2 rounded-card border border-violet/40 bg-violet/5 p-2">
          <span className="px-1 text-sm font-medium text-violet">
            {selected.size} mục được chọn
          </span>
          <Button
            size="sm"
            variant="outline"
            disabled={publish.isPending}
            onClick={() => void runBulk("Visible")}
          >
            <Eye className="size-3.5" aria-hidden /> Hiện
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={bulk.isPending}
            onClick={() => void runBulk("Hidden")}
          >
            <EyeOff className="size-3.5" aria-hidden /> Ẩn
          </Button>
          <Button
            size="sm"
            variant="outline"
            onClick={() => setScheduleOpen((v) => !v)}
          >
            <CalendarClock className="size-3.5" aria-hidden /> Hẹn giờ
          </Button>
          {scheduleOpen ? (
            <BulkSchedulePopover
              onClose={() => setScheduleOpen(false)}
              onDone={(from, until) => void runBulk("Scheduled", from, until)}
            />
          ) : null}
        </div>
      ) : null}

      {/* Bảng */}
      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : rows.length === 0 ? (
        <EmptyState
          title={includeDeleted ? "Thùng rác trống" : "Chưa có tài liệu nào"}
          description={
            includeDeleted
              ? "Tài liệu xóa sẽ nằm ở đây 30 ngày."
              : "Tạo tài liệu đầu tiên của bạn."
          }
          action={
            <Link to="/gv/tai-lieu/moi">
              <Button variant="secondary" size="sm">
                <Plus className="size-4" aria-hidden /> Tạo từ file
              </Button>
            </Link>
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[900px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="w-10 p-2">
                  <input
                    type="checkbox"
                    aria-label="Chọn tất cả"
                    checked={allSelected}
                    onChange={toggleAll}
                    className="accent-violet"
                  />
                </th>
                <th className="p-2">Tiêu đề</th>
                <th className="p-2">Chuyên mục</th>
                <th className="p-2">Khối</th>
                <th className="p-2">Môn</th>
                <th className="p-2">Tuần</th>
                <th className="p-2">Hiển thị</th>
                <th className="p-2 text-right">Xem / Tải</th>
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
                  <td className="p-2">
                    {!r.isDeleted ? (
                      <input
                        type="checkbox"
                        aria-label={`Chọn ${r.title}`}
                        checked={selected.has(r.id)}
                        onChange={() => toggleOne(r.id)}
                        className="accent-violet"
                      />
                    ) : null}
                  </td>
                  <td className="max-w-64 p-2">
                    <Link
                      to={
                        r.isDeleted
                          ? `/gv/tai-lieu/${r.id}`
                          : `/gv/tai-lieu/${r.id}`
                      }
                      className={cn(
                        "line-clamp-2 font-medium hover:text-violet",
                        r.isDeleted && "text-muted line-through",
                      )}
                    >
                      {r.title}
                    </Link>
                    {r.moderationStatus === "PendingReview" ? (
                      <Badge className="mt-1 bg-warn/10 text-warn">
                        Chờ kiểm duyệt
                      </Badge>
                    ) : null}
                    {r.moderationStatus === "Rejected" ? (
                      <Badge className="mt-1 bg-redpen/10 text-redpen">
                        Bị trả lại
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
                  <td className="p-2">
                    {r.isDeleted ? (
                      <Badge className="bg-muted/10 text-muted">Đã xóa</Badge>
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
                  <td className="p-2 text-right tabular-nums text-muted">
                    {r.viewCount.toLocaleString("vi-VN")} /{" "}
                    {r.downloadCount.toLocaleString("vi-VN")}
                  </td>
                  <td className="p-2 text-muted">
                    {formatDateTime(r.createdAt)}
                  </td>
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
                            onClick={() => navigate(`/gv/tai-lieu/${r.id}`)}
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
                              navigate(`/gv/tai-lieu/${copy.id}`);
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
    </div>
  );
}

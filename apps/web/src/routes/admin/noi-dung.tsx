import { useState } from "react";
import { FileText, Star } from "lucide-react";
import {
  useAdminContent,
  useAdminPatchContent,
  useAdminSections,
} from "@/features/admin/api";
import type {
  AdminContentItem,
  AdminContentPatch,
} from "@/features/admin/types";
import { EmptyState } from "@/components/common/EmptyState";
import { Pagination } from "@/components/common/Pagination";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";
import { PUBLISH_STATE_LABEL, SCOPE_LABEL } from "./helpers";

/** /admin/noi-dung — mọi tài liệu & bài tập (kể cả Private, đã xóa) (spec §5.4). */
export function AdminContentPage() {
  const [kind, setKind] = useState<"document" | "quiz">("document");
  const [sectionId, setSectionId] = useState(0);
  const [q, setQ] = useState("");
  const [deleted, setDeleted] = useState(false);
  const [page, setPage] = useState(1);

  const { data: sections } = useAdminSections();
  const { data: paged, isPending } = useAdminContent({
    kind,
    sectionId: sectionId || undefined,
    q: q || undefined,
    deleted,
    page,
    pageSize: 24,
  });
  const patchContent = useAdminPatchContent();

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Nội dung</h1>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex rounded-btn border border-grid bg-white p-0.5">
            <TabButton
              active={kind === "document"}
              onClick={() => {
                setKind("document");
                setPage(1);
              }}
            >
              Tài liệu
            </TabButton>
            <TabButton
              active={kind === "quiz"}
              onClick={() => {
                setKind("quiz");
                setPage(1);
              }}
            >
              Bài tập
            </TabButton>
          </div>
          <select
            aria-label="Lọc theo chuyên mục"
            value={sectionId}
            onChange={(e) => {
              setSectionId(Number(e.target.value));
              setPage(1);
            }}
            className="h-10 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value={0}>Mọi chuyên mục</option>
            {(sections ?? [])
              .filter((s) => s.isActive)
              .map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
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
            placeholder="Tìm theo tiêu đề…"
            className="h-10 w-52 rounded-btn border border-grid bg-white px-3 text-sm outline-none focus:border-violet"
          />
          <label className="flex items-center gap-1.5 text-sm text-muted">
            <input
              type="checkbox"
              checked={deleted}
              onChange={(e) => {
                setDeleted(e.target.checked);
                setPage(1);
              }}
              className="size-4 accent-violet"
            />
            Đã xóa
          </label>
        </div>
      </div>

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : (paged?.items ?? []).length === 0 ? (
        <EmptyState
          icon={FileText}
          title={deleted ? "Không có nội dung đã xóa" : "Chưa có nội dung"}
          description={
            deleted
              ? "Nội dung bị xóa sẽ nằm ở đây 30 ngày trước khi xóa cứng."
              : "Thử đổi bộ lọc hoặc tìm kiếm."
          }
        />
      ) : (
        <>
          <div className="overflow-x-auto rounded-card border border-grid bg-white">
            <table className="w-full min-w-[1020px] text-sm">
              <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
                <tr>
                  <th className="p-2">Tiêu đề</th>
                  <th className="p-2">Chuyên mục</th>
                  <th className="p-2">Khối</th>
                  <th className="p-2">Tác giả</th>
                  <th className="p-2">Phạm vi</th>
                  <th className="p-2">Hiển thị</th>
                  <th className="p-2">Kiểm duyệt</th>
                  <th className="p-2 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {(paged?.items ?? []).map((item) => (
                  <ContentRow
                    key={item.kind + item.id}
                    item={item}
                    busy={patchContent.isPending}
                    patch={(body) =>
                      patchContent.mutate({
                        kind: item.kind,
                        id: item.id,
                        body,
                      })
                    }
                  />
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

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-btn px-3 py-1.5 text-sm ${
        active
          ? "bg-violet text-white"
          : "text-muted hover:bg-paper hover:text-ink"
      }`}
    >
      {children}
    </button>
  );
}

function ContentRow({
  item,
  busy,
  patch,
}: {
  item: AdminContentItem;
  busy: boolean;
  patch: (body: AdminContentPatch) => void;
}) {
  const hidden = item.publishMode === "Hidden";
  return (
    <tr className="border-b border-grid/60 last:border-0 hover:bg-paper/50">
      <td className="max-w-64 p-2">
        <div className="flex items-center gap-1">
          {item.isFeatured ? (
            <Star className="size-3.5 shrink-0 text-warn" aria-hidden />
          ) : null}
          <span className="truncate font-medium" title={item.title}>
            {item.title}
          </span>
        </div>
        <div className="text-xs text-muted">
          {item.kind === "quiz"
            ? `${item.questionCount ?? 0} câu`
            : formatDateTime(item.createdAt)}
          {item.isDeleted ? " · Đã xóa" : ""}
        </div>
      </td>
      <td className="p-2 text-muted">{item.sectionName ?? "—"}</td>
      <td className="p-2 text-muted">
        {item.gradeId != null
          ? (item.gradeName ?? `Khối ${item.gradeId}`)
          : "—"}
      </td>
      <td className="max-w-40 p-2">
        <div
          className="truncate text-muted"
          title={item.ownerName ?? undefined}
        >
          {item.ownerName ?? "—"}
        </div>
        <div className="truncate text-xs text-muted">{item.teamName ?? ""}</div>
      </td>
      <td className="p-2 text-muted">
        {SCOPE_LABEL[item.scope] ?? item.scope}
      </td>
      <td className="p-2">
        <PublishStateBadge state={item.publishState} />
        <div className="mt-0.5 text-xs text-muted">
          {item.publishFrom ? `từ ${formatDateTime(item.publishFrom)}` : ""}
          {item.publishFrom && item.publishUntil ? " · " : ""}
          {item.publishUntil ? `đến ${formatDateTime(item.publishUntil)}` : ""}
        </div>
      </td>
      <td className="p-2">
        <ModerationBadge status={item.moderationStatus} />
      </td>
      <td className="p-2">
        <div className="flex flex-wrap justify-end gap-1">
          <Button
            size="sm"
            variant={hidden ? "secondary" : "outline"}
            disabled={busy}
            onClick={() =>
              patch({
                publishMode: hidden ? "Visible" : "Hidden",
              })
            }
          >
            {hidden ? "Hiện" : "Ẩn"}
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={busy}
            onClick={() => patch({ isFeatured: !item.isFeatured })}
          >
            {item.isFeatured ? "Bỏ nổi bật" : "Nổi bật"}
          </Button>
          {item.moderationStatus === "PendingReview" ? (
            <>
              <Button
                size="sm"
                variant="outline"
                disabled={busy}
                onClick={() => patch({ moderationStatus: "Approved" })}
              >
                Duyệt
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={busy}
                onClick={() => {
                  const note = window.prompt("Lý do trả lại (tùy chọn):");
                  if (note === null) return;
                  patch({
                    moderationStatus: "Rejected",
                    moderationNote: note.trim() || null,
                  });
                }}
              >
                Trả lại
              </Button>
            </>
          ) : null}
          {item.isDeleted ? (
            <Button
              size="sm"
              disabled={busy}
              onClick={() => patch({ isDeleted: false })}
            >
              Khôi phục
            </Button>
          ) : (
            <Button
              size="sm"
              variant="ghost"
              className="text-redpen hover:text-redpen"
              disabled={busy}
              onClick={() => {
                if (
                  window.confirm(
                    `Xóa "${item.title}"? Nội dung giữ 30 ngày trong vùng đã xóa trước khi xóa vĩnh viễn.`,
                  )
                )
                  patch({ isDeleted: true });
              }}
            >
              Xóa
            </Button>
          )}
        </div>
      </td>
    </tr>
  );
}

function PublishStateBadge({ state }: { state: string }) {
  const cls =
    state === "Visible"
      ? "bg-correct/10 text-correct"
      : state === "Hidden"
        ? "bg-warn/10 text-warn"
        : state === "Closed"
          ? "bg-muted/10 text-muted"
          : "bg-violet/10 text-violet";
  return <Badge className={cls}>{PUBLISH_STATE_LABEL[state] ?? state}</Badge>;
}

function ModerationBadge({ status }: { status: string }) {
  if (status === "Approved")
    return <Badge className="bg-correct/10 text-correct">Đã duyệt</Badge>;
  if (status === "PendingReview")
    return <Badge className="bg-warn/10 text-warn">Chờ duyệt</Badge>;
  return <Badge className="bg-wrong/10 text-wrong">Trả lại</Badge>;
}

import { useState } from "react";
import { Megaphone, X } from "lucide-react";
import {
  useAdminAnnouncements,
  useAdminDeleteAnnouncement,
  useAdminPages,
  useAdminSaveAnnouncement,
  useAdminSavePage,
  useAdminUpdateAnnouncement,
} from "@/features/admin/api";
import { useAdminTeams } from "@/features/teams/api";
import type { AdminAnnouncement } from "@/features/admin/types";
import { EmptyState } from "@/components/common/EmptyState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";
import { AUDIENCE_LABEL } from "./helpers";

/** /admin/thong-bao — thông báo (Công khai/GV/Tổ) + trang tĩnh (spec §5.4). */
export function AdminAnnouncementsPage() {
  const { data: announcements, isPending } = useAdminAnnouncements();
  const { data: pages } = useAdminPages();
  const del = useAdminDeleteAnnouncement();
  const [editing, setEditing] = useState<
    | { kind: "announcement"; item: AdminAnnouncement | null }
    | { kind: "page"; slug: string; title: string; body: string }
    | null
  >(null);

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
      <div>
        <h1 className="mb-4 text-xl font-semibold">Thông báo</h1>
        {(announcements ?? []).length === 0 ? (
          <EmptyState
            icon={Megaphone}
            title="Chưa có thông báo nào"
            description="Đăng thông báo công khai, cho giáo viên hoặc cho từng tổ."
            action={
              <Button
                size="sm"
                onClick={() => setEditing({ kind: "announcement", item: null })}
              >
                Đăng thông báo
              </Button>
            }
          />
        ) : (
          <>
            <div className="overflow-x-auto rounded-card border border-grid bg-white">
              <table className="w-full min-w-[760px] text-sm">
                <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
                  <tr>
                    <th className="p-2">Tiêu đề</th>
                    <th className="p-2">Đối tượng</th>
                    <th className="p-2">Mở từ</th>
                    <th className="p-2">Hết hạn</th>
                    <th className="p-2">Đăng lúc</th>
                    <th className="p-2 text-right">Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {(announcements ?? []).map((a) => (
                    <tr
                      key={a.id}
                      className="border-b border-grid/60 last:border-0 hover:bg-paper/50"
                    >
                      <td className="max-w-72 p-2">
                        <div className="truncate font-medium" title={a.title}>
                          {a.isPinned ? "📌 " : ""}
                          {a.title}
                        </div>
                      </td>
                      <td className="p-2 text-muted">
                        {AUDIENCE_LABEL[a.audience] ?? a.audience}
                        {a.audience === "Team" && a.teamName
                          ? ` (${a.teamName})`
                          : ""}
                      </td>
                      <td className="p-2 text-muted">
                        {a.publishAt ? formatDateTime(a.publishAt) : "Ngay"}
                      </td>
                      <td className="p-2 text-muted">
                        {a.expireAt ? formatDateTime(a.expireAt) : "Không"}
                      </td>
                      <td className="p-2 text-muted">
                        {formatDateTime(a.createdAt)}
                      </td>
                      <td className="p-2">
                        <div className="flex justify-end gap-1">
                          <Button
                            size="sm"
                            variant="outline"
                            onClick={() =>
                              setEditing({ kind: "announcement", item: a })
                            }
                          >
                            Sửa
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            className="text-redpen hover:text-redpen"
                            disabled={del.isPending}
                            onClick={() => {
                              if (window.confirm(`Xóa thông báo "${a.title}"?`))
                                void del.mutateAsync(a.id);
                            }}
                          >
                            Xóa
                          </Button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Button
              className="mt-3"
              size="sm"
              onClick={() => setEditing({ kind: "announcement", item: null })}
            >
              Đăng thông báo mới
            </Button>
          </>
        )}
      </div>

      <section>
        <h2 className="mb-2 text-base font-semibold">Trang tĩnh</h2>
        <p className="mb-3 text-sm text-muted">
          Nội dung Markdown hiển thị tại <code>/trang/&lt;slug&gt;</code> (Giới
          thiệu, Hướng dẫn, Chính sách dữ liệu…).
        </p>
        <div className="overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[560px] text-sm">
            <thead className="border-b border-grid bg-paper text-left text-xs text-muted">
              <tr>
                <th className="p-2">Slug</th>
                <th className="p-2">Tiêu đề</th>
                <th className="p-2">Cập nhật</th>
                <th className="p-2 text-right">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {(pages ?? []).map((p) => (
                <tr
                  key={p.slug}
                  className="border-b border-grid/60 last:border-0 hover:bg-paper/50"
                >
                  <td className="p-2 font-mono text-xs">{p.slug}</td>
                  <td className="p-2 font-medium">{p.title}</td>
                  <td className="p-2 text-muted">
                    {p.updatedAt ? formatDateTime(p.updatedAt) : "—"}
                  </td>
                  <td className="p-2">
                    <div className="flex justify-end">
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() =>
                          setEditing({
                            kind: "page",
                            slug: p.slug,
                            title: p.title,
                            body: p.bodyMarkdown,
                          })
                        }
                      >
                        Sửa
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {editing?.kind === "announcement" ? (
        <AnnouncementModal
          item={editing.item}
          onClose={() => setEditing(null)}
        />
      ) : null}
      {editing?.kind === "page" ? (
        <PageModal initial={editing} onClose={() => setEditing(null)} />
      ) : null}
    </div>
  );
}

function toLocalInput(iso: string | null): string {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(
    d.getHours(),
  )}:${pad(d.getMinutes())}`;
}

function AnnouncementModal({
  item,
  onClose,
}: {
  item: AdminAnnouncement | null;
  onClose: () => void;
}) {
  const { data: teams } = useAdminTeams();
  const create = useAdminSaveAnnouncement();
  const update = useAdminUpdateAnnouncement();
  const [title, setTitle] = useState(item?.title ?? "");
  const [body, setBody] = useState(item?.bodyHtml ?? "");
  const [audience, setAudience] = useState(item?.audience ?? "Public");
  const [teamId, setTeamId] = useState(item?.teamId ?? 0);
  const [pinned, setPinned] = useState(item?.isPinned ?? false);
  const [publishAt, setPublishAt] = useState(
    toLocalInput(item?.publishAt ?? null),
  );
  const [expireAt, setExpireAt] = useState(
    toLocalInput(item?.expireAt ?? null),
  );
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setError(null);
    const payload = {
      title: title.trim(),
      bodyHtml: body,
      audience,
      teamId: audience === "Team" ? teamId : undefined,
      isPinned: pinned,
      publishAt: publishAt ? new Date(publishAt).toISOString() : null,
      expireAt: expireAt ? new Date(expireAt).toISOString() : null,
    };
    try {
      if (item) await update.mutateAsync({ id: item.id, body: payload });
      else await create.mutateAsync(payload);
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không lưu được thông báo");
    }
  };

  const field =
    "h-10 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet";

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label="Thông báo"
    >
      <form
        className="max-h-[90vh] w-full max-w-xl overflow-y-auto rounded-card bg-white p-5"
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <div className="mb-3 flex items-center justify-between">
          <h2 className="font-semibold">
            {item ? "Sửa thông báo" : "Đăng thông báo"}
          </h2>
          <Button type="button" variant="ghost" size="sm" onClick={onClose}>
            <X className="size-4" aria-hidden />
          </Button>
        </div>
        <label className="mb-1 block text-sm font-medium">Tiêu đề</label>
        <input
          type="text"
          required
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          className={`${field} mb-3`}
        />
        <label className="mb-1 block text-sm font-medium">
          Nội dung (HTML)
        </label>
        <textarea
          required
          rows={5}
          value={body}
          onChange={(e) => setBody(e.target.value)}
          className="mb-3 w-full rounded-btn border border-grid p-2 text-sm outline-none focus:border-violet"
        />
        <div className="mb-3 grid gap-3 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-sm font-medium">Đối tượng</span>
            <select
              value={audience}
              onChange={(e) =>
                setAudience(e.target.value as "Public" | "Teachers" | "Team")
              }
              className={field}
            >
              <option value="Public">Công khai</option>
              <option value="Teachers">Giáo viên</option>
              <option value="Team">Tổ</option>
            </select>
          </label>
          {audience === "Team" ? (
            <label className="block">
              <span className="mb-1 block text-sm font-medium">Tổ</span>
              <select
                value={teamId}
                onChange={(e) => setTeamId(Number(e.target.value))}
                className={field}
              >
                <option value={0}>Chọn tổ…</option>
                {(teams ?? []).map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
          <label className="block">
            <span className="mb-1 block text-sm font-medium">
              Mở từ (tùy chọn)
            </span>
            <input
              type="datetime-local"
              value={publishAt}
              onChange={(e) => setPublishAt(e.target.value)}
              className={field}
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium">
              Hết hạn (tùy chọn)
            </span>
            <input
              type="datetime-local"
              value={expireAt}
              onChange={(e) => setExpireAt(e.target.value)}
              className={field}
            />
          </label>
        </div>
        <label className="mb-3 flex items-center gap-1.5 text-sm">
          <input
            type="checkbox"
            checked={pinned}
            onChange={(e) => setPinned(e.target.checked)}
            className="size-4 accent-violet"
          />
          Ghim (hiện nổi bật trên trang chủ)
        </label>
        {error ? (
          <p className="mb-3 text-sm text-redpen" role="alert">
            {error}
          </p>
        ) : null}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Hủy
          </Button>
          <Button type="submit" disabled={create.isPending || update.isPending}>
            {item ? "Lưu" : "Đăng"}
          </Button>
        </div>
      </form>
    </div>
  );
}

function PageModal({
  initial,
  onClose,
}: {
  initial: { slug: string; title: string; body: string };
  onClose: () => void;
}) {
  const save = useAdminSavePage();
  const [title, setTitle] = useState(initial.title);
  const [body, setBody] = useState(initial.body);
  const [error, setError] = useState<string | null>(null);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={`Sửa trang ${initial.slug}`}
    >
      <form
        className="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-card bg-white p-5"
        onSubmit={async (e) => {
          e.preventDefault();
          setError(null);
          try {
            await save.mutateAsync({
              slug: initial.slug,
              title: title.trim(),
              bodyMarkdown: body,
            });
            onClose();
          } catch (err) {
            setError(
              err instanceof Error ? err.message : "Không lưu được trang",
            );
          }
        }}
      >
        <div className="mb-3 flex items-center justify-between">
          <h2 className="font-semibold">
            Trang tĩnh{" "}
            <span className="font-mono text-sm text-muted">
              /{initial.slug}
            </span>
          </h2>
          <Button type="button" variant="ghost" size="sm" onClick={onClose}>
            <X className="size-4" aria-hidden />
          </Button>
        </div>
        <label className="mb-1 block text-sm font-medium">Tiêu đề</label>
        <input
          type="text"
          required
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          className="mb-3 h-10 w-full rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
        />
        <label className="mb-1 block text-sm font-medium">
          Nội dung (Markdown)
        </label>
        <textarea
          rows={12}
          value={body}
          onChange={(e) => setBody(e.target.value)}
          className="w-full rounded-btn border border-grid p-2 font-mono text-sm outline-none focus:border-violet"
        />
        {error ? (
          <p className="mb-3 mt-2 text-sm text-redpen" role="alert">
            {error}
          </p>
        ) : null}
        <div className="mt-3 flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Hủy
          </Button>
          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Đang lưu…" : "Lưu trang"}
          </Button>
        </div>
      </form>
    </div>
  );
}

import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { ChevronRight, Download, Eye, Flag, Heart, Users } from "lucide-react";
import {
  usePublicDocument,
  usePublicDocumentRelated,
  useSubmitReport,
  useAddFavorite,
} from "@/features/documents/api";
import { useMe } from "@/features/auth/api";
import { PagePreview } from "@/components/common/PagePreview";
import { ItemGrid } from "@/components/common/ItemGrid";
import { EmptyState } from "@/components/common/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/date";

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** /tai-lieu/:slugId (URL /{slug}-{id}) — chi tiết tài liệu công khai (spec §5.1). */
export function TaiLieuPage() {
  const { slugId: routeSlug = "" } = useParams<{ slugId: string }>();
  const match = /^(.*)-(\d+)$/.exec(routeSlug);
  const id = match ? Number(match[2]) : NaN;
  const navigate = useNavigate();

  const { data, isPending, isError } = usePublicDocument(
    Number.isFinite(id) ? id : null,
  );
  const { data: related } = usePublicDocumentRelated(
    data?.id ?? (Number.isFinite(id) ? id : null),
  );
  const { data: me } = useMe();
  const report = useSubmitReport();
  const addFavorite = useAddFavorite();
  const [saved, setSaved] = useState(false);
  const [reportOpen, setReportOpen] = useState(false);
  const [reportSent, setReportSent] = useState(false);
  const [reason, setReason] = useState("");
  const [detail, setDetail] = useState("");

  // Slug trong URL sai → về slug đúng (spec §8.4: 301 ở BE, FE redirect 301-soft).
  useEffect(() => {
    if (data && match && data.slug !== match[1]) {
      navigate(`/tai-lieu/${data.slug}-${data.id}`, { replace: true });
    }
  }, [data, match, navigate]);

  const [fileId, setFileId] = useState<number | null>(null);
  const selectedFileId = useMemo(() => {
    if (data == null) return null;
    if (fileId != null && data.files.some((f) => f.id === fileId))
      return fileId;
    return data.files[0]?.id ?? null;
  }, [data, fileId]);

  if (isPending)
    return (
      <div className="mx-auto w-full max-w-4xl px-4 py-8">
        <Skeleton className="mb-4 h-6 w-2/3" />
        <Skeleton className="mb-6 h-10 w-1/2" />
        <Skeleton className="aspect-[1/1.414] w-full rounded-card" />
      </div>
    );

  if (isError || data == null)
    return (
      <div className="mx-auto w-full max-w-4xl px-4 py-8">
        <EmptyState
          title="Không tìm thấy tài liệu"
          description="Tài liệu không tồn tại, đã bị gỡ, hoặc chưa được mở."
          action={
            <Link to="/" className="text-sm text-violet hover:underline">
              Về trang chủ
            </Link>
          }
        />
      </div>
    );

  const canDownload = me != null || data.allowGuestDownload;

  return (
    <div className="o-li">
      <div className="mx-auto w-full max-w-4xl px-4 py-8">
        {/* Breadcrumb */}
        <nav
          aria-label="Breadcrumb"
          className="mb-3 flex items-center gap-1 text-sm text-muted"
        >
          <Link to="/" className="hover:text-violet">
            Trang chủ
          </Link>
          <ChevronRight className="size-3.5" aria-hidden />
          {data.sectionSlug ? (
            <>
              <Link
                to={`/chuyen-muc/${data.sectionSlug}`}
                className="hover:text-violet"
              >
                {data.sectionName}
              </Link>
              <ChevronRight className="size-3.5" aria-hidden />
            </>
          ) : null}
          <span className="line-clamp-1 text-ink">{data.title}</span>
        </nav>

        <h1 className="mb-3 text-2xl font-semibold leading-snug">
          {data.title}
        </h1>

        {/* Nhãn */}
        <div className="mb-3 flex flex-wrap gap-1.5">
          {data.sectionName ? <Badge>{data.sectionName}</Badge> : null}
          {data.grade != null ? (
            <Badge variant="outline">Khối {data.grade}</Badge>
          ) : null}
          {data.subjectName ? (
            <Badge variant="outline">{data.subjectName}</Badge>
          ) : null}
          {data.weekNo != null ? (
            <Badge variant="outline">Tuần {data.weekNo}</Badge>
          ) : null}
          {data.schoolYearName ? (
            <Badge variant="outline">{data.schoolYearName}</Badge>
          ) : null}
        </div>

        {/* Tác giả + số liệu */}
        <div className="mb-5 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-muted">
          {data.author ? (
            <span className="flex items-center gap-1.5">
              <Users className="size-4" aria-hidden />
              GV {data.author.fullName}
              {data.author.teamName ? ` · ${data.author.teamName}` : ""}
            </span>
          ) : null}
          <span className="flex items-center gap-1.5">
            <Eye className="size-4" aria-hidden />{" "}
            {data.viewCount.toLocaleString("vi-VN")} lượt xem
          </span>
          <span>Đăng lúc {formatDateTime(data.createdAt)}</span>
        </div>

        {/* Tóm tắt + mô tả */}
        {data.summary ? (
          <p className="mb-3 text-base text-ink">{data.summary}</p>
        ) : null}
        {data.descriptionHtml ? (
          <div
            className="mb-6 max-w-prose text-base leading-relaxed [&_iframe]:my-3 [&_iframe]:aspect-video [&_iframe]:h-auto [&_iframe]:w-full [&_iframe]:rounded-card [&_iframe]:border [&_iframe]:border-grid [&_table]:w-full [&_table]:border-collapse [&_td]:border [&_td]:border-grid [&_td]:p-1.5 [&_th]:border [&_th]:border-grid [&_th]:p-1.5"
            dangerouslySetInnerHTML={{ __html: data.descriptionHtml }}
          />
        ) : null}

        {/* File + preview */}
        {data.files.length > 0 ? (
          <section aria-label="File tài liệu" className="mb-8">
            <div
              className="mb-3 flex flex-wrap gap-2"
              role="tablist"
              aria-label="Chọn file"
            >
              {data.files.map((f) => (
                <button
                  key={f.id}
                  type="button"
                  role="tab"
                  aria-selected={f.id === selectedFileId}
                  onClick={() => setFileId(f.id)}
                  className={
                    f.id === selectedFileId
                      ? "rounded-btn border border-violet bg-violet/10 px-3 py-1.5 text-sm font-medium text-violet"
                      : "rounded-btn border border-grid bg-white px-3 py-1.5 text-sm text-muted hover:border-violet/50 hover:text-violet"
                  }
                >
                  {f.name}
                </button>
              ))}
            </div>

            {selectedFileId != null ? (
              <PagePreview fileId={selectedFileId} title={data.title} />
            ) : null}

            <ul className="mt-4 space-y-2">
              {data.files.map((f) => (
                <li
                  key={f.id}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-card border border-grid bg-white p-3"
                >
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-medium">
                      {f.name}
                    </span>
                    <span className="text-xs text-muted">
                      {formatBytes(f.size)}
                      {f.pages ? ` · ${f.pages} trang` : ""}
                      {!f.ready ? " · đang xử lý preview" : ""}
                    </span>
                  </span>
                  {canDownload ? (
                    <a
                      href={`/api/files/${f.id}/download`}
                      className="inline-flex h-9 items-center gap-1.5 rounded-btn border border-grid bg-white px-3 text-sm text-ink transition hover:border-violet/40 hover:text-violet"
                    >
                      <Download className="size-4" aria-hidden /> Tải về
                    </a>
                  ) : null}
                </li>
              ))}
            </ul>
          </section>
        ) : null}

        {/* Hành động */}
        <div className="mb-8 flex flex-wrap gap-2">
          {me != null ? (
            <Button
              variant={saved ? "secondary" : "outline"}
              disabled={saved || addFavorite.isPending}
              onClick={async () => {
                try {
                  await addFavorite.mutateAsync({
                    itemType: "document",
                    itemId: data.id,
                  });
                  setSaved(true);
                } catch {
                  /* thông báo lỗi qua toast sau; giữ nút bấm lại */
                }
              }}
            >
              <Heart className="size-4" aria-hidden />
              {saved ? "Đã lưu vào yêu thích" : "Lưu vào yêu thích"}
            </Button>
          ) : null}
          <Button variant="ghost" onClick={() => setReportOpen(true)}>
            <Flag className="size-4" aria-hidden /> Báo lỗi nội dung
          </Button>
          {reportSent ? (
            <span className="flex items-center gap-1 text-sm text-correct">
              Đã nhận báo lỗi — cảm ơn bạn!
            </span>
          ) : null}
        </div>

        {/* Tài liệu liên quan */}
        {related && related.length > 0 ? (
          <section
            aria-label="Tài liệu liên quan"
            className="border-t border-grid pt-6"
          >
            <h2 className="mb-3 text-lg font-semibold">Tài liệu liên quan</h2>
            <ItemGrid items={related} />
          </section>
        ) : null}
      </div>

      {/* Modal báo lỗi */}
      {reportOpen ? (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
          role="dialog"
          aria-modal="true"
          aria-label="Báo lỗi nội dung"
        >
          <form
            className="w-full max-w-md rounded-card bg-white p-5"
            onSubmit={async (e) => {
              e.preventDefault();
              try {
                await report.mutateAsync({
                  itemType: "document",
                  itemId: data.id,
                  reason: reason.trim(),
                  detail: detail.trim() || undefined,
                });
                setReportOpen(false);
                setReportSent(true);
                setReason("");
                setDetail("");
              } catch {
                /* giữ modal mở, lỗi hiện qua title */
              }
            }}
          >
            <h2 className="mb-2 font-semibold">Báo lỗi nội dung</h2>
            <p className="mb-3 text-sm text-muted">
              Mô tả vấn đề để quản trị viên kiểm tra. Bạn có thể để trống tên.
            </p>
            <label className="mb-1 block text-sm font-medium">
              Lý do (bắt buộc)
            </label>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={2}
              required
              className="mb-3 w-full rounded-btn border border-grid p-2 text-sm outline-none focus:border-violet"
            />
            <label className="mb-1 block text-sm font-medium">
              Chi tiết thêm
            </label>
            <textarea
              value={detail}
              onChange={(e) => setDetail(e.target.value)}
              rows={3}
              className="mb-4 w-full rounded-btn border border-grid p-2 text-sm outline-none focus:border-violet"
            />
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setReportOpen(false)}>
                Hủy
              </Button>
              <Button
                type="submit"
                disabled={report.isPending || !reason.trim()}
              >
                Gửi báo lỗi
              </Button>
            </div>
          </form>
        </div>
      ) : null}
    </div>
  );
}

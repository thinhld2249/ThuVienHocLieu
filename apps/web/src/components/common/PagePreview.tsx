import { useState } from "react";
import { Maximize2, X } from "lucide-react";
import { useFilePages } from "@/features/documents/api";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";

function PageView({
  url,
  kind,
  page,
  title,
  className,
}: {
  url: string;
  kind: string;
  page: number;
  title: string;
  className?: string;
}) {
  const label = `Trang ${page} của ${title}`;
  if (kind === "video")
    return (
      <video
        src={url}
        controls
        aria-label={label}
        className={`w-full rounded-card border border-grid ${className ?? ""}`}
      />
    );
  if (kind === "image")
    return (
      <img
        src={url}
        alt={label}
        loading="lazy"
        className={`w-full rounded-card border border-grid ${className ?? ""}`}
      />
    );
  // còn lại = pdf (local stream qua token; cloudinary là ảnh trang)
  return (
    <iframe
      src={url}
      title={label}
      className={`aspect-[1/1.414] w-full rounded-card border border-grid bg-white ${className ?? ""}`}
    />
  );
}

/**
 * Preview từng trang của 1 file (spec §5.1, §11.3): cuộn dọc, lazy-load,
 * nút xem toàn màn hình. Pages rỗng = file đang xử lý → poll 5s.
 */
export function PagePreview({
  fileId,
  title,
}: {
  fileId: number;
  title: string;
}) {
  const { data, isPending } = useFilePages(fileId, 1, 10);
  const [fullscreen, setFullscreen] = useState(false);

  const pages = data?.pages ?? [];
  return (
    <div className="relative space-y-4">
      {pages.length === 0 ? (
        <div className="space-y-2">
          <Skeleton className="aspect-[1/1.414] w-full rounded-card" />
          <p className="text-center text-sm text-muted">
            {isPending
              ? "Đang tải preview…"
              : "File đang được xử lý — trang sẽ tự hiện khi xong."}
          </p>
        </div>
      ) : (
        <>
          {pages.map((p) => (
            <PageView
              key={p.number}
              url={p.url}
              kind={p.kind}
              page={p.number}
              title={title}
            />
          ))}
          <div className="flex justify-center">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setFullscreen(true)}
            >
              <Maximize2 className="size-4" aria-hidden /> Xem toàn màn hình
            </Button>
          </div>
        </>
      )}

      {fullscreen ? (
        <div
          className="fixed inset-0 z-50 overflow-y-auto bg-black/90 p-4"
          role="dialog"
          aria-modal="true"
          aria-label={`Xem toàn màn hình ${title}`}
        >
          <button
            type="button"
            onClick={() => setFullscreen(false)}
            className="absolute right-4 top-4 z-10 rounded-btn bg-white/10 p-2 text-white transition hover:bg-white/20"
            aria-label="Đóng"
          >
            <X className="size-5" aria-hidden />
          </button>
          <div className="mx-auto flex max-w-3xl flex-col gap-4 pb-8">
            {pages.map((p) => (
              <PageView
                key={p.number}
                url={p.url}
                kind={p.kind}
                page={p.number}
                title={title}
              />
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

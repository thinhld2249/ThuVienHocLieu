import { useRef, useState } from "react";
import { FileUp, Loader2, Trash2, XCircle } from "lucide-react";
import { cn } from "@/lib/utils";
import type { UploadItem } from "@/features/files/api";
import { Button } from "@/components/ui/button";

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function ItemRow({
  item,
  onRemove,
}: {
  item: UploadItem;
  onRemove: (key: string) => void;
}) {
  const busy = item.status === "uploading" || item.status === "processing";
  return (
    <li className="flex items-center gap-3 rounded-btn border border-grid bg-white p-2">
      <FileUp className="size-4 shrink-0 text-violet" aria-hidden />
      <div className="min-w-0 flex-1">
        <div className="flex items-baseline justify-between gap-2">
          <span className="truncate text-sm font-medium">{item.name}</span>
          <span className="shrink-0 text-xs text-muted">
            {formatSize(item.size)}
          </span>
        </div>
        {item.status === "uploading" ? (
          <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-grid">
            <div
              className="h-full rounded-full bg-violet transition-all"
              style={{ width: `${item.progress}%` }}
            />
          </div>
        ) : null}
        {item.status === "processing" ? (
          <span className="mt-0.5 flex items-center gap-1 text-xs text-muted">
            <Loader2 className="size-3 animate-spin" aria-hidden />
            Đang tạo preview…
          </span>
        ) : null}
        {item.status === "error" ? (
          <span className="mt-0.5 flex items-center gap-1 text-xs text-redpen">
            <XCircle className="size-3" aria-hidden /> {item.error}
          </span>
        ) : null}
        {item.status === "ready" ? (
          <span className="mt-0.5 text-xs text-correct">Sẵn sàng</span>
        ) : null}
      </div>
      <Button
        variant="ghost"
        size="icon"
        className="size-8 shrink-0"
        onClick={() => onRemove(item.key)}
        aria-label={`Bỏ file ${item.name}`}
        disabled={busy}
      >
        <Trash2 className="size-4" aria-hidden />
      </Button>
    </li>
  );
}

/**
 * Kéo-thả nhiều file (≤ 10, spec §5.2): tiến trình upload, trạng thái preview,
 * bỏ file. Component được điều khiển: form sở hữu useFileUploads và đọc
 * fileIds/busy khi submit.
 */
export function FileDropzone({
  maxFiles = 10,
  items,
  onAdd,
  onRemove,
}: {
  maxFiles?: number;
  items: UploadItem[];
  onAdd: (files: FileList | File[]) => void;
  onRemove: (key: string) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);

  return (
    <div>
      <div
        role="button"
        tabIndex={0}
        aria-label="Chọn file để tải lên"
        onClick={() => inputRef.current?.click()}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            inputRef.current?.click();
          }
        }}
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragOver(false);
          if (e.dataTransfer.files.length > 0) onAdd(e.dataTransfer.files);
        }}
        className={cn(
          "flex cursor-pointer flex-col items-center justify-center gap-2 rounded-card border-2 border-dashed bg-white/60 px-4 py-8 text-center transition",
          dragOver
            ? "border-violet bg-violet/5"
            : "border-grid hover:border-violet/50",
        )}
      >
        <FileUp className="size-8 text-muted/60" aria-hidden />
        <p className="text-sm font-medium">
          Kéo-thả file vào đây hoặc bấm để chọn
        </p>
        <p className="text-xs text-muted">
          Tối đa {maxFiles} file · pdf, office, ảnh, video (theo cài đặt hệ
          thống)
        </p>
        <input
          ref={inputRef}
          type="file"
          multiple
          hidden
          accept=".pdf,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.jpg,.jpeg,.png,.webp,.mp4"
          onChange={(e) => {
            if (e.target.files?.length) onAdd(e.target.files);
            e.target.value = "";
          }}
        />
      </div>
      {items.length > 0 ? (
        <ul className="mt-3 space-y-2">
          {items.map((it) => (
            <ItemRow key={it.key} item={it} onRemove={onRemove} />
          ))}
        </ul>
      ) : null}
    </div>
  );
}

import { useCallback, useRef, useState } from "react";
import { api } from "@/lib/api-client";
import { apiErrorTitle } from "@/features/auth/api";
import type { FileDto } from "@/features/documents/types";

/**
 * Trạng thái 1 file trong FileDropzone. Upload qua XMLHttpRequest để lấy
 * onProgress (openapi-fetch/fetch không hỗ trợ % upload); khi BE trả về,
 * poll /api/teacher/files/{id} cho tới khi preview Ready/Failed.
 */
export interface UploadItem {
  key: string;
  name: string;
  ext: string;
  size: number;
  progress: number;
  status: "uploading" | "processing" | "ready" | "error";
  error?: string;
  file?: FileDto;
}

const POLL_MS = 2000;
const POLL_TIMEOUT_MS = 120_000;
let seq = 0;

function extOf(name: string): string {
  const i = name.lastIndexOf(".");
  return i >= 0 ? name.slice(i + 1).toLowerCase() : "";
}

function uploadOne(
  file: File,
  onProgress: (key: string, pct: number) => void,
): Promise<FileDto> {
  const key = `up-${++seq}`;
  const form = new FormData();
  form.append("file", file);

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", "/api/teacher/files");
    xhr.withCredentials = true;
    xhr.setRequestHeader("X-Requested-With", "hoclieu");
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable)
        onProgress(key, Math.round((e.loaded / e.total) * 100));
    };
    xhr.onload = () => {
      let body: unknown;
      try {
        body = JSON.parse(xhr.responseText);
      } catch {
        reject(new Error(`Upload "${file.name}" thất bại`));
        return;
      }
      if (xhr.status >= 200 && xhr.status < 300) resolve(body as FileDto);
      else
        reject(
          new Error((body as { title?: string })?.title ?? "Upload thất bại"),
        );
    };
    xhr.onerror = () => reject(new Error("Mất kết nối khi upload"));
    xhr.send(form);
  });
}

async function pollUntilReady(
  fileId: number,
  startedAt: number,
): Promise<FileDto> {
  // Bắt đầu bằng bản BE vừa trả về để không bỏ lỡ file nhẹ đã Ready.
  const check = async (prev?: FileDto): Promise<FileDto | undefined> => {
    const { data, error } = await api.GET("/api/teacher/files/{id}", {
      params: { path: { id: String(fileId) } },
    });
    if (error || !data)
      throw new Error(apiErrorTitle(error, "Không kiểm tra được file"));
    const f = data as unknown as FileDto;
    if (
      f.processingStatus === "Ready" ||
      f.processingStatus === "NotApplicable"
    )
      return f;
    if (f.processingStatus === "Failed")
      throw new Error(f.processingError ?? "Xử lý preview thất bại");
    if (Date.now() - startedAt > POLL_TIMEOUT_MS) {
      // Hết thời gian poll mà vẫn Processing → vẫn dùng file (preview chờ sau).
      return f;
    }
    return prev;
  };

  for (;;) {
    const { data } = await api.GET("/api/teacher/files/{id}", {
      params: { path: { id: String(fileId) } },
    });
    const first = data as unknown as FileDto;
    if (
      first.processingStatus === "Ready" ||
      first.processingStatus === "NotApplicable"
    )
      return first;
    if (first.processingStatus === "Failed")
      throw new Error(first.processingError ?? "Xử lý preview thất bại");
    while (Date.now() - startedAt <= POLL_TIMEOUT_MS) {
      await new Promise((r) => setTimeout(r, POLL_MS));
      const f = await check();
      if (f) return f;
    }
    return first;
  }
}

/**
 * Hook quản lý danh sách file trong form tài liệu (≤ 10, spec §5.2).
 * `fileIds` chỉ gồm file đã upload xong (cho phép gắn tài liệu).
 */
export function useFileUploads(maxFiles = 10) {
  const [items, setItems] = useState<UploadItem[]>([]);
  const itemsRef = useRef(items);
  itemsRef.current = items;

  const setFor = useCallback((key: string, patch: Partial<UploadItem>) => {
    setItems((prev) =>
      prev.map((it) => (it.key === key ? { ...it, ...patch } : it)),
    );
  }, []);

  const addFiles = useCallback(
    async (files: FileList | File[]) => {
      const list = Array.from(files);
      const room = maxFiles - itemsRef.current.length;
      const accepted = list.slice(0, Math.max(0, room));
      for (const file of accepted) {
        const key = `up-${++seq}`;
        setItems((prev) =>
          prev.length >= maxFiles
            ? prev
            : [
                ...prev,
                {
                  key,
                  name: file.name,
                  ext: extOf(file.name),
                  size: file.size,
                  progress: 0,
                  status: "uploading",
                },
              ],
        );
        try {
          const dto = await uploadOne(file, (k, pct) =>
            setFor(k, { progress: pct }),
          );
          setFor(key, { file: dto, status: "processing", progress: 100 });
          const final = await pollUntilReady(dto.id, Date.now());
          setFor(key, { file: final, status: "ready" });
        } catch (e) {
          setFor(key, {
            status: "error",
            error: e instanceof Error ? e.message : "Upload thất bại",
          });
        }
      }
    },
    [maxFiles, setFor],
  );

  const remove = useCallback((key: string) => {
    setItems((prev) => prev.filter((it) => it.key !== key));
  }, []);

  const clear = useCallback(() => setItems([]), []);

  const fileIds = items
    .filter((it) => it.status !== "error" && it.file)
    .map((it) => it.file!.id);
  const busy = items.some(
    (it) => it.status === "uploading" || it.status === "processing",
  );

  return { items, addFiles, remove, clear, fileIds, busy };
}

import { useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import { apiErrorTitle } from "@/features/auth/api";
import type {
  BulkPublishResult,
  CreateDocumentRequest,
  DocumentDetail,
  FavoriteRow,
  FilePagesDto,
  MyDocumentRow,
  Paged,
  PublicDocumentDetail,
  PublicItemRow,
  PublishRequest,
  UpdateDocumentRequest,
} from "./types";

function unwrap<T>(data: unknown, error: unknown, fallback: string): T {
  if (error || !data) {
    // Gắn body ProblemDetails gốc vào lỗi → form đọc được 422 errors / 409 code.
    throw Object.assign(new Error(apiErrorTitle(error, fallback)), {
      data: error,
    });
  }
  return data as unknown as T;
}

// ===== Khu công khai (spec §5.1, §10) =====

export interface PublicItemsParams {
  kind?: "document" | "quiz" | null;
  section?: string | null;
  grade?: number | null;
  subject?: string | null;
  year?: number | null;
  week?: number | null;
  q?: string | null;
  sort?: "new" | "popular" | null;
  page?: number;
  pageSize?: number;
}

/** Danh sách gộp tài liệu + bài tập đang hiện (404 = không thấy, không có trong kết quả). */
export function usePublicItems(params: PublicItemsParams, enabled = true) {
  const page = params.page ?? 1;
  const pageSize = params.pageSize ?? 24;
  return useQuery({
    queryKey: ["public", "items", { ...params, page, pageSize }],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/public/items", {
        params: {
          query: {
            kind: params.kind ?? undefined,
            section: params.section ?? undefined,
            grade: params.grade ?? undefined,
            subject: params.subject ?? undefined,
            year: params.year ?? undefined,
            week: params.week ?? undefined,
            q: params.q ?? undefined,
            sort: params.sort ?? "new",
            page,
            pageSize,
          } as never,
        },
      });
      return unwrap<Paged<PublicItemRow>>(
        data,
        error,
        "Không tải được danh sách nội dung",
      );
    },
  });
}

/**
 * Danh sách gộp tài liệu + bài tập (trang "Tất cả" / tìm kiếm).
 * BE trả từng kind riêng → fetch 2 phía (mỗi phía lấy đủ đến hết cửa sổ trang),
 * gộp + sắp xếp trong bộ nhớ rồi cắt trang (đủ cho quy mô 500 user).
 */
export function useCombinedItems(
  params: Omit<PublicItemsParams, "kind">,
  enabled = true,
) {
  const page = params.page ?? 1;
  const pageSize = params.pageSize ?? 24;
  const take = page * pageSize;
  const docs = usePublicItems(
    { ...params, kind: "document", page: 1, pageSize: take },
    enabled,
  );
  const quizzes = usePublicItems(
    { ...params, kind: "quiz", page: 1, pageSize: take },
    enabled,
  );

  const merged = useMemo(() => {
    const a = [...(docs.data?.items ?? []), ...(quizzes.data?.items ?? [])];
    if (params.sort === "popular") {
      a.sort(
        (x, y) =>
          y.viewCount - x.viewCount || (x.createdAt < y.createdAt ? 1 : -1),
      );
    } else {
      a.sort((x, y) => (x.createdAt < y.createdAt ? 1 : -1));
    }
    return a;
  }, [docs.data, quizzes.data, params.sort]);

  const start = (page - 1) * pageSize;
  return {
    items: merged.slice(start, start + pageSize),
    total: (docs.data?.total ?? 0) + (quizzes.data?.total ?? 0),
    loading: docs.isPending || quizzes.isPending,
  };
}

export function usePublicDocument(id: number | null) {
  return useQuery({
    queryKey: ["public", "document", id],
    enabled: id != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/public/documents/{id}", {
        params: { path: { id: String(id) } },
      });
      return unwrap<PublicDocumentDetail>(
        data,
        error,
        "Không tìm thấy tài liệu",
      );
    },
  });
}

export function usePublicDocumentRelated(id: number | null) {
  return useQuery({
    queryKey: ["public", "document", id, "related"],
    enabled: id != null,
    queryFn: async () => {
      const { data, error } = await api.GET(
        "/api/public/documents/{id}/related",
        { params: { path: { id: String(id) } } },
      );
      return unwrap<PublicItemRow[]>(
        data,
        error,
        "Không tải được tài liệu liên quan",
      );
    },
  });
}

/** URL preview từng trang; pages rỗng = file đang xử lý → FE poll lại. */
export function useFilePages(fileId: number | null, from = 1, count = 10) {
  return useQuery({
    queryKey: ["files", fileId, "pages", from, count],
    enabled: fileId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/files/{id}/pages", {
        params: {
          path: { id: String(fileId) },
          query: { from, count } as never,
        },
      });
      return unwrap<FilePagesDto>(data, error, "Không tải được preview");
    },
    // Chưa có trang (đang xử lý) → thử lại 5s; có trang rồi thì dừng.
    refetchInterval: (query) =>
      query.state.data?.pages.length === 0 ? 5000 : false,
  });
}

export function useSubmitReport() {
  return useMutation({
    mutationFn: async (body: {
      itemType: string;
      itemId: number;
      reason: string;
      detail?: string;
    }) => {
      const { error } = await api.POST("/api/public/reports", {
        body: body as never,
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Gửi báo lỗi không thành công"));
    },
  });
}

// ===== Giáo viên: tài liệu của tôi (spec §5.2) =====

export interface MyDocumentsParams {
  section?: string | null;
  grade?: number | null;
  q?: string | null;
  includeDeleted?: boolean;
  page?: number;
  pageSize?: number;
}

export function useMyDocuments(params: MyDocumentsParams) {
  const page = params.page ?? 1;
  const pageSize = params.pageSize ?? 20;
  return useQuery({
    queryKey: ["teacher", "documents", { ...params, page, pageSize }],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teacher/documents", {
        params: {
          query: {
            section: params.section ?? undefined,
            grade: params.grade ?? undefined,
            q: params.q ?? undefined,
            includeDeleted: params.includeDeleted || undefined,
            page,
            pageSize,
          } as never,
        },
      });
      return unwrap<Paged<MyDocumentRow>>(
        data,
        error,
        "Không tải được danh sách tài liệu",
      );
    },
  });
}

export function useMyDocument(id: number | null) {
  return useQuery({
    queryKey: ["teacher", "document", id],
    enabled: id != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teacher/documents/{id}", {
        params: { path: { id: String(id) } },
      });
      return unwrap<DocumentDetail>(data, error, "Không tìm thấy tài liệu");
    },
  });
}

export function useCreateDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateDocumentRequest) => {
      const { data, error } = await api.POST("/api/teacher/documents", {
        body: body as never,
      });
      return unwrap<{ id: number }>(data, error, "Không tạo được tài liệu");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "documents"] });
    },
  });
}

export function useUpdateDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      body,
    }: {
      id: number;
      body: UpdateDocumentRequest;
    }) => {
      const { data, error } = await api.PUT("/api/teacher/documents/{id}", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      return unwrap<DocumentDetail>(data, error, "Không lưu được tài liệu");
    },
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: ["teacher", "documents"] });
      qc.invalidateQueries({ queryKey: ["teacher", "document", vars.id] });
    },
  });
}

export function useDeleteDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE("/api/teacher/documents/{id}", {
        params: { path: { id: String(id) } },
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không xóa được tài liệu"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "documents"] });
    },
  });
}

export function useRestoreDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.POST("/api/teacher/documents/{id}/restore", {
        params: { path: { id: String(id) } },
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không khôi phục được tài liệu"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "documents"] });
    },
  });
}

export function useDuplicateDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { data, error } = await api.POST(
        "/api/teacher/documents/{id}/duplicate",
        { params: { path: { id: String(id) } } },
      );
      return unwrap<{ id: number }>(
        data,
        error,
        "Không nhân bản được tài liệu",
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "documents"] });
    },
  });
}

/** Hiện/Ẩn/Hẹn giờ 1 nội dung (spec §4.4). */
export function usePublishContent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: number; body: PublishRequest }) => {
      const { error } = await api.PATCH("/api/teacher/documents/{id}/publish", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      if (error)
        throw new Error(
          apiErrorTitle(error, "Không đổi được trạng thái hiển thị"),
        );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "documents"] });
      qc.invalidateQueries({ queryKey: ["teacher", "document"] });
      qc.invalidateQueries({ queryKey: ["teacher", "quiz"] });
      qc.invalidateQueries({ queryKey: ["public"] });
    },
  });
}

/** Hiện/Ẩn/Hẹn giờ nhiều nội dung cùng lúc. */
export function useBulkPublish() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: {
      items: { kind: string; id: number }[];
      mode: PublishRequest["mode"];
      from?: string | null;
      until?: string | null;
    }) => {
      const { data, error } = await api.POST(
        "/api/teacher/content/publish-bulk",
        { body: body as never },
      );
      return unwrap<BulkPublishResult>(
        data,
        error,
        "Không thao tác hàng loạt được",
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "documents"] });
      qc.invalidateQueries({ queryKey: ["teacher", "document"] });
      qc.invalidateQueries({ queryKey: ["teacher", "quiz"] });
      qc.invalidateQueries({ queryKey: ["public"] });
    },
  });
}

// ===== Yêu thích (spec §5.2) =====

export function useFavorites() {
  return useQuery({
    queryKey: ["teacher", "favorites"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teacher/favorites");
      return unwrap<FavoriteRow[]>(data, error, "Không tải được yêu thích");
    },
  });
}

export function useAddFavorite() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { itemType: string; itemId: number }) => {
      const { error } = await api.POST("/api/teacher/favorites", {
        body: body as never,
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không lưu được vào yêu thích"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "favorites"] });
    },
  });
}

export function useRemoveFavorite() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      itemType,
      itemId,
    }: {
      itemType: string;
      itemId: number;
    }) => {
      const { error } = await api.DELETE(
        "/api/teacher/favorites/{itemType}/{itemId}",
        { params: { path: { itemType, itemId: String(itemId) } } },
      );
      if (error)
        throw new Error(apiErrorTitle(error, "Không bỏ yêu thích được"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "favorites"] });
    },
  });
}

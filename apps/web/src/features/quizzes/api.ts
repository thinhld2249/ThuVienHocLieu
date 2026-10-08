import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import { apiErrorTitle } from "@/features/auth/api";
import type { Paged } from "@/features/documents/types";
import type {
  AttemptRow,
  CreateQuizRequest,
  MyQuizRow,
  QuizDetail,
  QuizImportResult,
  QuizStats,
  UpdateQuizRequest,
} from "./types";
import type { PublishRequest } from "@/features/documents/types";

function unwrap<T>(data: unknown, error: unknown, fallback: string): T {
  if (error || !data)
    throw Object.assign(new Error(apiErrorTitle(error, fallback)), {
      data: error,
    });
  return data as unknown as T;
}

export interface MyQuizzesParams {
  section?: string | null;
  grade?: number | null;
  q?: string | null;
  includeDeleted?: boolean;
  page?: number;
  pageSize?: number;
}

export function useMyQuizzes(params: MyQuizzesParams) {
  const page = params.page ?? 1;
  const pageSize = params.pageSize ?? 20;
  return useQuery({
    queryKey: ["teacher", "quizzes", { ...params, page, pageSize }],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teacher/quizzes", {
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
      return unwrap<Paged<MyQuizRow>>(
        data,
        error,
        "Không tải được danh sách bài tập",
      );
    },
  });
}

export function useMyQuiz(id: number | null) {
  return useQuery({
    queryKey: ["teacher", "quiz", id],
    enabled: id != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teacher/quizzes/{id}", {
        params: { path: { id: String(id) } },
      });
      return unwrap<QuizDetail>(data, error, "Không tìm thấy bài tập");
    },
  });
}

export function useCreateQuiz() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateQuizRequest) => {
      const { data, error } = await api.POST("/api/teacher/quizzes", {
        body: body as never,
      });
      return unwrap<{ id: number }>(data, error, "Không tạo được bài tập");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "quizzes"] });
    },
  });
}

export interface UpdateQuizVars {
  id: number;
  body: UpdateQuizRequest;
  confirmRegrade?: boolean;
}

/**
 * PUT thay toàn bộ. BE trả 409:
 * - code "conflict" → updatedAt lệch (ai đó đã sửa)
 * - code "quiz.has_attempts" + affectedAttempts → xác nhận chấm lại rồi PUT lại với confirmRegrade.
 */
export function useUpdateQuiz() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body, confirmRegrade }: UpdateQuizVars) => {
      const { data, error } = await api.PUT("/api/teacher/quizzes/{id}", {
        params: {
          path: { id: String(id) },
          query: confirmRegrade ? { confirmRegrade: true } : {},
        },
        body: body as never,
      });
      if (error || !data)
        throw Object.assign(
          new Error(apiErrorTitle(error, "Không lưu được bài tập")),
          {
            data: error,
          },
        );
      return data as unknown as QuizDetail;
    },
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: ["teacher", "quizzes"] });
      qc.invalidateQueries({ queryKey: ["teacher", "quiz", vars.id] });
      qc.invalidateQueries({ queryKey: ["public"] });
    },
  });
}

export function useDeleteQuiz() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE("/api/teacher/quizzes/{id}", {
        params: { path: { id: String(id) } },
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không xóa được bài tập"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "quizzes"] });
    },
  });
}

export function useRestoreQuiz() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.POST("/api/teacher/quizzes/{id}/restore", {
        params: { path: { id: String(id) } },
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không khôi phục được bài tập"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "quizzes"] });
    },
  });
}

export function useDuplicateQuiz() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { data, error } = await api.POST(
        "/api/teacher/quizzes/{id}/duplicate",
        { params: { path: { id: String(id) } } },
      );
      return unwrap<{ id: number }>(data, error, "Không nhân bản được bài tập");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "quizzes"] });
    },
  });
}

/** Hiện/Ẩn/Hẹn giờ 1 bài tập (spec §4.4). */
export function usePublishQuiz() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: number; body: PublishRequest }) => {
      const { error } = await api.PATCH("/api/teacher/quizzes/{id}/publish", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      if (error)
        throw new Error(
          apiErrorTitle(error, "Không đổi được trạng thái hiển thị"),
        );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teacher", "quizzes"] });
      qc.invalidateQueries({ queryKey: ["teacher", "quiz"] });
      qc.invalidateQueries({ queryKey: ["public"] });
    },
  });
}

/**
 * Import .docx/.xlsx → tạo quiz (Hidden) + danh sách cảnh báo.
 * XHR multipart (cần CSRF header, không cần progress).
 */
export function importQuiz(
  kind: "docx" | "xlsx",
  file: File,
  sectionId?: number | null,
): Promise<QuizImportResult> {
  const url =
    kind === "docx"
      ? "/api/teacher/quizzes/import/docx"
      : "/api/teacher/quizzes/import/xlsx";
  const form = new FormData();
  form.append("file", file);
  if (sectionId != null) form.append("sectionId", String(sectionId));

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", url);
    xhr.withCredentials = true;
    xhr.setRequestHeader("X-Requested-With", "hoclieu");
    xhr.onload = () => {
      let body: unknown;
      try {
        body = JSON.parse(xhr.responseText);
      } catch {
        reject(new Error(`Nhập file "${file.name}" thất bại`));
        return;
      }
      if (xhr.status >= 200 && xhr.status < 300)
        resolve(body as QuizImportResult);
      else
        reject(
          new Error(
            (body as { title?: string })?.title ?? "Nhập file thất bại",
          ),
        );
    };
    xhr.onerror = () => reject(new Error("Mất kết nối khi nhập file"));
    xhr.send(form);
  });
}

// ===== Kết quả & thống kê (spec §6.8) =====

export interface QuizAttemptsParams {
  assignmentId?: number | null;
  which?: "best" | "first" | "last" | null;
  page?: number;
  pageSize?: number;
}

export function useQuizAttempts(
  quizId: number | null,
  params: QuizAttemptsParams,
) {
  const page = params.page ?? 1;
  const pageSize = params.pageSize ?? 20;
  return useQuery({
    queryKey: [
      "teacher",
      "quiz",
      quizId,
      "attempts",
      { ...params, page, pageSize },
    ],
    enabled: quizId != null,
    queryFn: async () => {
      const { data, error } = await api.GET(
        "/api/teacher/quizzes/{id}/attempts",
        {
          params: {
            path: { id: String(quizId) },
            query: {
              assignmentId: params.assignmentId ?? undefined,
              which: params.which ?? undefined,
              page,
              pageSize,
            } as never,
          },
        },
      );
      return unwrap<Paged<AttemptRow>>(data, error, "Không tải được kết quả");
    },
  });
}

export function useDeleteAttempt(quizId: number | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (attemptId: string) => {
      const { error } = await api.DELETE(
        "/api/teacher/quizzes/{id}/attempts/{attemptId}",
        { params: { path: { id: String(quizId), attemptId } } },
      );
      if (error)
        throw new Error(apiErrorTitle(error, "Không xóa được lượt làm"));
    },
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({
        queryKey: ["teacher", "quiz", quizId, "attempts"],
      });
      qc.invalidateQueries({ queryKey: ["teacher", "quiz", quizId, "stats"] });
      void vars;
    },
  });
}

export function useQuizStats(quizId: number | null) {
  return useQuery({
    queryKey: ["teacher", "quiz", quizId, "stats"],
    enabled: quizId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teacher/quizzes/{id}/stats", {
        params: { path: { id: String(quizId) } },
      });
      return unwrap<QuizStats>(data, error, "Không tải được thống kê");
    },
  });
}

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import { apiErrorTitle } from "@/features/auth/api";
import type {
  AttemptDto,
  LocalAttemptRef,
  PublicQuizDetail,
  SavedAnswer,
} from "./types";

function unwrap<T>(data: unknown, error: unknown, fallback: string): T {
  if (error || !data)
    throw Object.assign(new Error(apiErrorTitle(error, fallback)), {
      data: error,
    });
  return data as unknown as T;
}

// ===== Giới thiệu quiz (spec §5.1) =====

export function usePublicQuiz(id: number | null) {
  return useQuery({
    queryKey: ["public", "quiz", id],
    enabled: id != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/public/quizzes/{id}", {
        params: { path: { id: String(id) } },
      });
      return unwrap<PublicQuizDetail>(data, error, "Không tìm thấy bài tập");
    },
  });
}

// ===== Làm bài (spec §6.6) =====

export function useCreateAttempt(quizId: number | null) {
  return useMutation({
    mutationFn: async ({
      guestName,
      guestClass,
    }: {
      guestName?: string;
      guestClass?: string;
    }) => {
      const { data, error } = await api.POST(
        "/api/public/quizzes/{id}/attempts",
        {
          params: { path: { id: String(quizId) } },
          body: {
            guestName: guestName ?? null,
            guestClass: guestClass ?? null,
          } as never,
        },
      );
      if (error || !data)
        throw Object.assign(
          new Error(apiErrorTitle(error, "Không bắt đầu được bài làm")),
          { data: error },
        );
      return data as unknown as AttemptDto;
    },
  });
}

export function useAttempt(attemptId: string | null) {
  return useQuery({
    queryKey: ["public", "attempt", attemptId],
    enabled: attemptId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/public/attempts/{id}", {
        params: { path: { id: String(attemptId) } },
      });
      return unwrap<AttemptDto>(data, error, "Không tìm thấy lượt làm bài");
    },
  });
}

/**
 * Lưu câu trả lời theo lô (FE debounce 1,5s — spec §6.6).
 * Gửi toàn bộ bản đồ câu trả lời hiện tại (đơn giản, ≤ 300 câu).
 * 410 (Gone) → attempt đã chốt → FE chuyển trang kết quả.
 */
export function useSaveAnswers(attemptId: string | null) {
  return useMutation({
    mutationFn: async (answers: SavedAnswer[]) => {
      const { data, error } = await api.PUT(
        "/api/public/attempts/{id}/answers",
        {
          params: { path: { id: String(attemptId) } },
          body: answers as never,
        },
      );
      if (error || !data)
        throw Object.assign(
          new Error(apiErrorTitle(error, "Không lưu được câu trả lời")),
          { data: error },
        );
      return data as { saved: number };
    },
  });
}

export function useSubmitAttempt(attemptId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST(
        "/api/public/attempts/{id}/submit",
        { params: { path: { id: String(attemptId) } } },
      );
      if (error || !data)
        throw Object.assign(
          new Error(apiErrorTitle(error, "Nộp bài không thành công")),
          { data: error },
        );
      return data as unknown as AttemptDto;
    },
    onSuccess: () => {
      removeLocalAttempt(attemptId!);
      qc.invalidateQueries({
        queryKey: ["public", "attempt", attemptId],
      });
    },
  });
}

// ===== localStorage: khôi phục khi tải lại/mất mạng (spec §6.6) =====

const ATTEMPTS_KEY = "hl_attempts";
const answersKey = (attemptId: string) => `hl_answers_${attemptId}`;

function readAttempts(): LocalAttemptRef[] {
  try {
    const raw = localStorage.getItem(ATTEMPTS_KEY);
    if (!raw) return [];
    const list = JSON.parse(raw);
    return Array.isArray(list) ? (list as LocalAttemptRef[]) : [];
  } catch {
    return [];
  }
}

export function listLocalAttempts(): LocalAttemptRef[] {
  return readAttempts().sort((a, b) => (a.startedAt < b.startedAt ? 1 : -1));
}

export function addLocalAttempt(ref: Omit<LocalAttemptRef, "startedAt">) {
  const list = readAttempts().filter((a) => a.attemptId !== ref.attemptId);
  list.push({ ...ref, startedAt: new Date().toISOString() });
  localStorage.setItem(ATTEMPTS_KEY, JSON.stringify(list.slice(-20)));
}

export function removeLocalAttempt(attemptId: string) {
  const list = readAttempts().filter((a) => a.attemptId !== attemptId);
  localStorage.setItem(ATTEMPTS_KEY, JSON.stringify(list));
}

export function readLocalAnswers(attemptId: string): Map<number, number[]> {
  try {
    const raw = localStorage.getItem(answersKey(attemptId));
    if (!raw) return new Map();
    const list = JSON.parse(raw) as SavedAnswer[];
    return new Map(list.map((a) => [a.questionId, a.optionIds]));
  } catch {
    return new Map();
  }
}

export function writeLocalAnswers(
  attemptId: string,
  answers: Map<number, number[]>,
) {
  const list: SavedAnswer[] = [...answers.entries()]
    .filter(([, ids]) => ids.length > 0)
    .map(([questionId, optionIds]) => ({ questionId, optionIds }));
  localStorage.setItem(answersKey(attemptId), JSON.stringify(list));
}

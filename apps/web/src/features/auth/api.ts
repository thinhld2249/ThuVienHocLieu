import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import type {
  ApiError,
  InvitationInfo,
  Me,
  PublicTeam,
  UpdateMeRequest,
} from "./types";

/** Trích body ProblemDetails từ `error` của openapi-fetch (body JSON đã parse). */
export function apiErrorBody(error: unknown): ApiError | undefined {
  const e = error as { data?: ApiError } | ApiError | undefined;
  if (!e) return undefined;
  // openapi-fetch: `error` chính là body đã parse; một số chỗ truyền cả result { data }.
  if ("title" in e || "errors" in e) return e as ApiError;
  return e.data;
}

/** Trích lỗi ProblemDetails (title tiếng Việt) từ response của openapi-fetch. */
export function apiErrorTitle(error: unknown, fallback: string): string {
  return apiErrorBody(error)?.title ?? fallback;
}

/**
 * Trạng thái đăng nhập = query `me` (spec §8.5).
 * 401 (chưa đăng nhập) → data = null, KHÔNG coi là lỗi.
 */
export function useMe() {
  return useQuery({
    queryKey: ["me"],
    queryFn: async (): Promise<Me | null> => {
      const { data, error, response } = await api.GET("/api/me");
      if (response.status === 401 || (error && !data)) return null;
      if (error || !data) throw new Error("Không tải được thông tin tài khoản");
      return data as unknown as Me;
    },
    // /me nhẹ: cho phép tự làm mới (focus tab, điều hướng) để vai trò/tổ cập nhật
    // sau khi được duyệt hoặc bổ nhiệm — không cần F5.
    staleTime: 30_000,
    retry: (count, err) => {
      const status = (err as { status?: number })?.status;
      return status !== 401 && count < 1;
    },
  });
}

/** POST /api/auth/google — trả Me (spec §3.1). */
export function useLogin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      idToken,
      inviteToken,
    }: {
      idToken: string;
      inviteToken?: string;
    }): Promise<Me> => {
      const { data, error } = await api.POST("/api/auth/google", {
        // OpenAPI build này chưa xuất requestBody → type body là undefined, cast bỏ qua
        body: (inviteToken ? { idToken, inviteToken } : { idToken }) as never,
      });
      if (error || !data)
        throw new Error(apiErrorTitle(error, "Đăng nhập không thành công"));
      return data as unknown as Me;
    },
    onSuccess: (me) => {
      qc.setQueryData(["me"], me);
    },
  });
}

/** POST /api/auth/login — email + mật khẩu (tài khoản đã đặt mật khẩu). */
export function useLoginPassword() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      email,
      password,
    }: {
      email: string;
      password: string;
    }): Promise<Me> => {
      const { data, error } = await api.POST("/api/auth/login", {
        body: { email, password } as never,
      });
      if (error || !data)
        throw new Error(apiErrorTitle(error, "Đăng nhập không thành công"));
      return data as unknown as Me;
    },
    onSuccess: (me) => {
      qc.setQueryData(["me"], me);
    },
  });
}

export function useUpdateMe() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: UpdateMeRequest): Promise<Me> => {
      const { data, error } = await api.PUT("/api/me", {
        // OpenAPI build này chưa xuất requestBody → cast (như useLogin)
        body: body as never,
      });
      if (error || !data)
        throw new Error(apiErrorTitle(error, "Không cập nhật được thông tin"));
      return data as unknown as Me;
    },
    onSuccess: (me) => {
      qc.setQueryData(["me"], me);
    },
  });
}

/** PUT /api/me/password — đặt/đổi mật khẩu đăng nhập của chính mình (trang Hồ sơ). */
export function useSetPassword() {
  return useMutation({
    mutationFn: async (newPassword: string) => {
      const { error } = await api.PUT("/api/me/password", {
        body: { newPassword } as never,
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không đặt được mật khẩu"));
    },
  });
}

export function useLogout() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/auth/logout");
      if (error)
        throw new Error(apiErrorTitle(error, "Đăng xuất không thành công"));
    },
    onSuccess: () => {
      qc.setQueryData(["me"], null);
    },
  });
}

/** Đổi security_stamp — mọi thiết bị phải đăng nhập lại (spec §3.4). */
export function useLogoutAll() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/auth/logout-all");
      if (error) throw new Error(apiErrorTitle(error, "Không thực hiện được"));
    },
    onSuccess: () => {
      qc.setQueryData(["me"], null);
    },
  });
}

/** GET /api/public/teams — cho form /cho-duyet chọn tổ. */
export function usePublicTeams() {
  return useQuery({
    queryKey: ["public", "teams"],
    queryFn: async (): Promise<PublicTeam[]> => {
      const { data, error } = await api.GET("/api/public/teams");
      if (error || !data) throw new Error("Không tải được danh sách tổ");
      return data as unknown as PublicTeam[];
    },
  });
}

/** GET /api/invitations/{token} — công khai, không lộ email đầy đủ. */
export function useInvitation(token: string | undefined) {
  return useQuery({
    queryKey: ["invitation", token],
    enabled: Boolean(token),
    queryFn: async (): Promise<InvitationInfo> => {
      const { data, error } = await api.GET("/api/invitations/{token}", {
        params: { path: { token: token! } },
      });
      if (error || !data)
        throw new Error(apiErrorTitle(error, "Lời mời không hợp lệ"));
      return data as unknown as InvitationInfo;
    },
  });
}

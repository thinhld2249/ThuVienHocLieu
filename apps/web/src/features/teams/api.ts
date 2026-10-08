import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import { apiErrorTitle } from "@/features/auth/api";
import type {
  AdminCreateInvitationsRequest,
  AdminUserDto,
  CreateInvitationsRequest,
  CreateTeamRequest,
  JoinActionResult,
  JoinRequestDto,
  InvitationDto,
  InvitationResult,
  NotificationDto,
  PostAnnouncementRequest,
  SetLeadRequest,
  TeamAnnouncementDto,
  TeamDetailDto,
  TeamDto,
  TeamMemberDto,
  TeamStatsDto,
  UpdateTeamRequest,
} from "./types";

function unwrap<T>(data: unknown, error: unknown, fallback: string): T {
  if (error || !data) throw new Error(apiErrorTitle(error, fallback));
  return data as unknown as T;
}

// ===== Tổ (thành viên gọi được) =====

export function useTeam(teamId: number | undefined) {
  return useQuery({
    queryKey: ["team", teamId],
    enabled: teamId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teams/{id}", {
        params: { path: { id: String(teamId) } },
      });
      return unwrap<TeamDetailDto>(data, error, "Không tải được thông tin tổ");
    },
  });
}

export function useTeamMembers(teamId: number | undefined) {
  return useQuery({
    queryKey: ["team", teamId, "members"],
    enabled: teamId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teams/{id}/members", {
        params: { path: { id: String(teamId) } },
      });
      return unwrap<TeamMemberDto[]>(
        data,
        error,
        "Không tải được danh sách thành viên",
      );
    },
  });
}

export function useJoinRequests(teamId: number | undefined) {
  return useQuery({
    queryKey: ["team", teamId, "join-requests"],
    enabled: teamId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teams/{id}/join-requests", {
        params: { path: { id: String(teamId) } },
      });
      return unwrap<JoinRequestDto[]>(
        data,
        error,
        "Không tải được yêu cầu tham gia",
      );
    },
  });
}

export function useTeamAnnouncements(teamId: number | undefined) {
  return useQuery({
    queryKey: ["team", teamId, "announcements"],
    enabled: teamId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teams/{id}/announcements", {
        params: { path: { id: String(teamId) } },
      });
      return unwrap<TeamAnnouncementDto[]>(
        data,
        error,
        "Không tải được thông báo tổ",
      );
    },
  });
}

export function useTeamStats(teamId: number | undefined) {
  return useQuery({
    queryKey: ["team", teamId, "stats"],
    enabled: teamId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teams/{id}/stats", {
        params: { path: { id: String(teamId) } },
      });
      return unwrap<TeamStatsDto>(data, error, "Không tải được thống kê tổ");
    },
  });
}

export function useInvitations(teamId: number | undefined) {
  return useQuery({
    queryKey: ["team", teamId, "invitations"],
    enabled: teamId != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teams/{id}/invitations", {
        params: { path: { id: String(teamId) } },
      });
      return unwrap<InvitationDto[]>(
        data,
        error,
        "Không tải được danh sách lời mời",
      );
    },
  });
}

// ===== Thao tác chờ duyệt (Lead | Deputy) =====

export function useApproveJoinRequest(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (userId: number) => {
      const { data, error } = await api.POST(
        "/api/teams/{id}/join-requests/{userId}/approve",
        { params: { path: { id: String(teamId), userId: String(userId) } } },
      );
      return unwrap<JoinActionResult>(data, error, "Không duyệt được");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId] });
    },
  });
}

export function useRejectJoinRequest(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      userId,
      reason,
    }: {
      userId: number;
      reason?: string;
    }) => {
      const { data, error } = await api.POST(
        "/api/teams/{id}/join-requests/{userId}/reject",
        {
          params: { path: { id: String(teamId), userId: String(userId) } },
          body: { reason: reason ?? null } as never,
        },
      );
      return unwrap<JoinActionResult>(data, error, "Không từ chối được");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId] });
    },
  });
}

export function useBulkApproveJoinRequests(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (userIds: number[]) => {
      const { data, error } = await api.POST(
        "/api/teams/{id}/join-requests/approve",
        {
          params: { path: { id: String(teamId) } },
          body: { userIds } as never,
        },
      );
      return unwrap<JoinActionResult[]>(
        data,
        error,
        "Không duyệt hàng loạt được",
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId] });
    },
  });
}

// ===== Thành viên (Lead) =====

export function useSetMemberRole(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      userId,
      role,
    }: {
      userId: number;
      role: "Member" | "Deputy";
    }) => {
      const { data, error } = await api.PATCH(
        "/api/teams/{id}/members/{userId}",
        {
          params: { path: { id: String(teamId), userId: String(userId) } },
          body: { role } as never,
        },
      );
      return unwrap<unknown>(data, error, "Không đổi được vai trò");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId] });
    },
  });
}

export function useRemoveMember(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (userId: number) => {
      const { error } = await api.DELETE("/api/teams/{id}/members/{userId}", {
        params: { path: { id: String(teamId), userId: String(userId) } },
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không gỡ được thành viên"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId] });
    },
  });
}

// ===== Lời mời (Lead | Deputy) =====

export function useCreateInvitations(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateInvitationsRequest) => {
      const { data, error } = await api.POST("/api/teams/{id}/invitations", {
        params: { path: { id: String(teamId) } },
        body: body as never,
      });
      return unwrap<InvitationResult[]>(data, error, "Không gửi được lời mời");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId, "invitations"] });
    },
  });
}

export function useResendInvitation(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (invitationId: string) => {
      const { data, error } = await api.POST(
        "/api/teams/{id}/invitations/{invId}/resend",
        { params: { path: { id: String(teamId), invId: invitationId } } },
      );
      return unwrap<InvitationResult>(data, error, "Không gửi lại được");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId, "invitations"] });
    },
  });
}

export function useRevokeInvitation(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (invitationId: string) => {
      const { error } = await api.DELETE(
        "/api/teams/{id}/invitations/{invId}",
        {
          params: { path: { id: String(teamId), invId: invitationId } },
        },
      );
      if (error)
        throw new Error(apiErrorTitle(error, "Không thu hồi được lời mời"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId, "invitations"] });
    },
  });
}

// ===== Thông báo tổ =====

export function usePostTeamAnnouncement(teamId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: PostAnnouncementRequest) => {
      const { data, error } = await api.POST("/api/teams/{id}/announcements", {
        params: { path: { id: String(teamId) } },
        body: body as never,
      });
      return unwrap<TeamAnnouncementDto>(
        data,
        error,
        "Không đăng được thông báo",
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId, "announcements"] });
    },
  });
}

// ===== Admin: tổ =====

export function useAdminTeams() {
  return useQuery({
    queryKey: ["admin", "teams"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/teams");
      return unwrap<TeamDto[]>(data, error, "Không tải được danh sách tổ");
    },
  });
}

export function useAdminCreateTeam() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateTeamRequest) => {
      const { data, error } = await api.POST("/api/admin/teams", {
        body: body as never,
      });
      return unwrap<TeamDto>(data, error, "Không tạo được tổ");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "teams"] });
    },
  });
}

export function useAdminUpdateTeam() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      body,
    }: {
      id: number;
      body: UpdateTeamRequest;
    }) => {
      const { data, error } = await api.PUT("/api/admin/teams/{id}", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      return unwrap<TeamDto>(data, error, "Không lưu được tổ");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "teams"] });
    },
  });
}

export function useAdminDeactivateTeam() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE("/api/admin/teams/{id}", {
        params: { path: { id: String(id) } },
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không ngưng hoạt động tổ được"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "teams"] });
    },
  });
}

export function useAdminSetLead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: number; body: SetLeadRequest }) => {
      const { data, error } = await api.PUT("/api/admin/teams/{id}/lead", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      return unwrap<TeamDto>(data, error, "Không bổ nhiệm tổ trưởng được");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "teams"] });
    },
  });
}

// ===== Admin: lời mời toàn hệ thống =====

export function useAdminInvitations(filters: {
  teamId?: number;
  status?: string;
}) {
  return useQuery({
    queryKey: ["admin", "invitations", filters],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/invitations", {
        params: {
          query: {
            teamId: filters.teamId,
            status: filters.status || undefined,
          } as never,
        },
      });
      return unwrap<InvitationDto[]>(
        data,
        error,
        "Không tải được danh sách lời mời",
      );
    },
  });
}

export function useAdminCreateInvitations() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: AdminCreateInvitationsRequest) => {
      const { data, error } = await api.POST("/api/admin/invitations", {
        body: body as never,
      });
      return unwrap<InvitationResult[]>(data, error, "Không gửi được lời mời");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "invitations"] });
    },
  });
}

// ===== Admin: tra cứu GV =====

export function useAdminUsers(params: { teamId?: number; q?: string }) {
  return useQuery({
    queryKey: ["admin", "users", params],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/users", {
        params: {
          query: {
            teamId: params.teamId,
            q: params.q || undefined,
            pageSize: 20,
          } as never,
        },
      });
      // M6: endpoint trả PagedResult — giữ contract mảng cho consumer M2 (bổ nhiệm tổ trưởng)
      const paged = unwrap<{ items: AdminUserDto[] }>(
        data,
        error,
        "Không tìm được giáo viên",
      );
      return paged.items;
    },
  });
}

// ===== Thông báo in-app =====

/** Chuột 30s — đủ cho thông báo duyệt/mời, không tốn tài nguyên (spec M2). */
export function useNotifications() {
  return useQuery({
    queryKey: ["me", "notifications"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/me/notifications/");
      if (error || !data) throw new Error("Không tải được thông báo");
      return data as unknown as {
        items: NotificationDto[];
        unreadCount: number;
      };
    },
    refetchInterval: 30_000,
    staleTime: 10_000,
  });
}

export function useMarkNotificationsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (ids?: number[]) => {
      const { data, error } = await api.POST("/api/me/notifications/read", {
        body: (ids && ids.length > 0 ? { ids } : null) as never,
      });
      return unwrap<{ updated: number }>(
        data,
        error,
        "Không đánh dấu đã đọc được",
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["me", "notifications"] });
    },
  });
}

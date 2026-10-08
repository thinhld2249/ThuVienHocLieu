// Gương DTO backend (Features/Teams/TeamDtos.cs).
// OpenAPI build .NET 8 này chưa xuất response schema → định nghĩa tay (spec §8.5).

import type { TeamRole } from "@/features/auth/types";

export interface TeamDto {
  id: number;
  name: string;
  description: string | null;
  gradeName: string | null;
  memberCount: number;
  leadName: string | null;
  isActive: boolean;
}

/** Role = vai trò của viewer trong tổ; Admin (không phải thành viên) → null. */
export interface TeamDetailDto {
  id: number;
  name: string;
  description: string | null;
  gradeName: string | null;
  memberCount: number;
  leadName: string | null;
  role: TeamRole | null;
  isAdmin: boolean;
}

export interface TeamMemberDto {
  userId: number;
  fullName: string;
  email: string;
  avatarUrl: string | null;
  role: TeamRole;
  joinedAt: string;
  contentCount: number;
}

export interface JoinRequestDto {
  userId: number;
  fullName: string;
  email: string;
  avatarUrl: string | null;
  phone: string | null;
  requestedAt: string;
}

export interface JoinActionResult {
  userId: number;
  fullName: string;
  ok: boolean;
  message: string | null;
}

/** Status: "Chờ" | "Đã nhận" | "Hết hạn" | "Đã thu hồi". */
export interface InvitationDto {
  id: string;
  email: string;
  status: string;
  link: string | null;
  expiresAt: string;
  createdAt: string;
  teamRole: TeamRole;
  inviterName: string | null;
  acceptedByName: string | null;
  teamName: string | null;
}

/**
 * Action: "added" (Active vào thẳng) · "approved" (Pending được duyệt) ·
 * "invited" (tạo lời mời) · "existing" (đã là thành viên) ·
 * "existing_invite" (đã có lời mời chờ) · "skipped" (bỏ qua, xem reason).
 */
export interface InvitationResult {
  email: string;
  action: string;
  reason: string | null;
  invitationId: string | null;
  link: string | null;
}

export interface TeamAnnouncementDto {
  id: number;
  title: string;
  bodyHtml: string;
  isPinned: boolean;
  authorName: string;
  createdAt: string;
}

export interface TeamStatsDto {
  memberCount: number;
  documentCount: number;
  quizCount: number;
  members: TeamMemberStatDto[];
}

export interface TeamMemberStatDto {
  userId: number;
  fullName: string;
  role: TeamRole;
  documentCount: number;
  quizCount: number;
}

// ===== Request bodies =====

export interface CreateTeamRequest {
  name: string;
  description?: string | null;
  gradeId?: number | null;
}

export interface UpdateTeamRequest {
  name?: string;
  description?: string | null;
  gradeId?: number | null;
  isActive?: boolean;
}

export interface SetLeadRequest {
  userId: number;
}

export interface CreateInvitationsRequest {
  emails: string[];
  message?: string | null;
}

export interface AdminCreateInvitationsRequest {
  teamId: number;
  teamRole?: TeamRole | null;
  emails: string[];
  message?: string | null;
}

export interface PostAnnouncementRequest {
  title: string;
  bodyHtml: string;
  isPinned: boolean;
  expireAt?: string | null;
}

/** GET /api/admin/users — tra cứu GV (bổ nhiệm tổ trưởng, phân công). M6: PagedResult<AdminUserDetail>. */
export interface AdminUserDto {
  id: number;
  fullName: string;
  email: string;
  status: string;
  systemRole: string;
  phone?: string | null;
  avatarUrl?: string | null;
  statusReason?: string | null;
  lastLoginAt?: string | null;
  requestedTeamName?: string | null;
  teams?: { teamId: number; teamName: string; role: string }[];
}

// ===== Thông báo in-app =====

export interface NotificationDto {
  id: number;
  type: string;
  title: string;
  link: string | null;
  createdAt: string;
  read: boolean;
}

export interface NotificationListResponse {
  items: NotificationDto[];
  unreadCount: number;
}

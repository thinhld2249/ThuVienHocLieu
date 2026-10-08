// Gương DTO backend (Features/Auth/AuthDtos.cs, TeamsEndpoints.cs).
// OpenAPI của build .NET 8 này chưa xuất response schema → định nghĩa tay,
// runtime validate bằng zod ở form (spec §8.5).

export type SystemRole = 'Teacher' | 'Admin'
export type UserStatus = 'Pending' | 'Active' | 'Suspended' | 'Rejected'
export type TeamRole = 'Member' | 'Deputy' | 'Lead'

export interface MeTeam {
  id: number
  name: string
  role: TeamRole
}

export interface Me {
  id: number
  email: string
  fullName: string
  phone: string | null
  avatarUrl: string | null
  systemRole: SystemRole
  status: UserStatus
  statusReason: string | null
  teams: MeTeam[]
  requestedTeamId: number | null
}

export interface UpdateMeRequest {
  fullName?: string
  phone?: string
  requestedTeamId?: number
}

export interface PublicTeam {
  id: number
  name: string
  gradeName: string | null
}

export interface InvitationInfo {
  emailMasked: string
  teamName: string
  expiresAt: string
  status: string
}

/** ProblemDetails RFC 9457 (spec §8.4) — title tiếng Việt, code máy đọc. */
export interface ApiError {
  title: string
  status?: number
  code?: string
  [key: string]: unknown
}

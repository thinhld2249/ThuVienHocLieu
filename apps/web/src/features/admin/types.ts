/** §5.4 — DTO khu Admin (M6). Khớp shape backend (AdminEndpoints, spec §10). */

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

// ===== Người dùng =====

export interface AdminUserTeam {
  teamId: number;
  teamName: string;
  role: string;
}

export interface AdminUserDetail {
  id: number;
  fullName: string;
  email: string;
  phone: string | null;
  avatarUrl: string | null;
  status: "Pending" | "Active" | "Suspended" | "Rejected";
  statusReason: string | null;
  systemRole: "Teacher" | "Admin";
  lastLoginAt: string | null;
  createdAt: string;
  requestedTeamId: number | null;
  requestedTeamName: string | null;
  teams: AdminUserTeam[];
}

export interface UserPatch {
  status?: string;
  statusReason?: string | null;
  systemRole?: string;
  teamIds?: number[];
}

// ===== Nội dung =====

export interface AdminContentItem {
  kind: "document" | "quiz";
  id: number;
  title: string;
  slug: string;
  sectionId: number | null;
  sectionName: string | null;
  gradeId: number | null;
  gradeName: string | null;
  ownerId: number;
  ownerName: string;
  teamId: number | null;
  teamName: string | null;
  scope: string;
  publishMode: string;
  publishState: string;
  publishFrom: string | null;
  publishUntil: string | null;
  moderationStatus: string;
  isFeatured: boolean;
  isDeleted: boolean;
  questionCount: number | null;
  viewCount: number | null;
  downloadCount: number | null;
  createdAt: string;
  updatedAt: string;
}

export interface AdminContentPatch {
  isFeatured?: boolean;
  publishMode?: "Hidden" | "Visible" | "Scheduled";
  publishFrom?: string;
  publishUntil?: string;
  moderationStatus?: string;
  moderationNote?: string | null;
  sectionId?: number;
  isDeleted?: boolean;
}

// ===== Báo cáo =====

export interface AdminReport {
  id: number;
  itemType: string;
  itemId: number;
  itemTitle: string | null;
  reason: string;
  detail: string | null;
  reporterUserId: number | null;
  reporterName: string | null;
  status: "Open" | "Resolved" | "Dismissed";
  note: string | null;
  createdAt: string;
}

// ===== Danh mục =====

export interface SectionAdmin {
  id: number;
  slug: string;
  name: string;
  icon: string | null;
  color: string | null;
  sort: number;
  contentKind: string;
  defaultPublishMode: string;
  defaultScope: string;
  requireWeek: boolean;
  isInternal: boolean;
  isActive: boolean;
}

export interface GradeAdmin {
  id: number;
  name: string;
  sort: number;
}

export interface SubjectAdmin {
  id: number;
  name: string;
  slug: string;
  sort: number;
  isActive: boolean;
}

export interface SchoolYearDto {
  id: number;
  name: string;
  startDate: string;
  endDate: string;
  isCurrent: boolean;
}

export interface TagDto {
  id: number;
  name: string;
  slug: string;
}

export interface RolloverResult {
  oldYearId: number;
  newYearId: number;
  archivedClasses: number;
  clonedClasses: number;
  clonedStudents: number;
}

// ===== Lớp =====

export interface AdminClass {
  id: number;
  name: string;
  gradeId: number | null;
  gradeName: string | null;
  schoolYearId: number;
  schoolYearName: string;
  teamId: number | null;
  teamName: string | null;
  homeroomTeacherId: number | null;
  homeroomTeacherName: string | null;
  activeStudentCount: number;
  isArchived: boolean;
  createdAt: string;
}

// ===== Thông báo & trang tĩnh =====

export interface AdminAnnouncement {
  id: number;
  title: string;
  bodyHtml: string;
  audience: "Public" | "Teachers" | "Team";
  teamId: number | null;
  teamName: string | null;
  isPinned: boolean;
  publishAt: string | null;
  expireAt: string | null;
  createdAt: string;
}

export interface AnnouncementRequest {
  title: string;
  bodyHtml: string;
  audience: "Public" | "Teachers" | "Team";
  teamId?: number;
  isPinned?: boolean;
  publishAt?: string | null;
  expireAt?: string | null;
}

export interface StaticPageDto {
  slug: string;
  title: string;
  bodyMarkdown: string;
  updatedAt: string;
}

// ===== Cài đặt =====

export interface SettingsMap {
  [key: string]: unknown;
}

// ===== Nhật ký =====

export interface AdminAuditLog {
  id: number;
  actorUserId: number | null;
  actorName: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  data: string | null;
  ip: string | null;
  createdAt: string;
}

// ===== Hệ thống =====

export interface SystemStatus {
  version: string;
  utcNow: string;
  health: { db: string; cloudinary: string; gotenberg: string };
  files: {
    pending: number;
    processing: number;
    ready: number;
    failed: number;
    notApplicable: number;
  };
  failedFiles: {
    id: number;
    originalName: string;
    processingError: string | null;
    processingAttempts: number;
    createdAt: string;
  }[];
}

// ===== Dashboard =====

export interface DashboardDto {
  users: {
    pending: number;
    active: number;
    suspended: number;
    rejected: number;
  };
  approvalQueue: {
    userId: number;
    fullName: string;
    email: string;
    avatarUrl: string | null;
    requestedTeamId: number | null;
    requestedTeamName: string | null;
    createdAt: string;
  }[];
  teamsWithoutLead: string[];
  contentNew: {
    documents7d: number;
    quizzes7d: number;
    documents30d: number;
    quizzes30d: number;
  };
  attempts: { last7d: number; inProgress: number };
  openReports: number;
  storage: { totalBytes: number; fileCount: number };
  failedFiles: {
    id: number;
    originalName: string;
    processingError: string | null;
    processingAttempts: number;
    createdAt: string;
  }[];
}

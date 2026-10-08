import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import { apiErrorTitle } from "@/features/auth/api";
import type {
  AdminAnnouncement,
  AdminAuditLog,
  AdminClass,
  AdminContentItem,
  AdminContentPatch,
  AdminReport,
  AnnouncementRequest,
  GradeAdmin,
  Paged,
  RolloverResult,
  SchoolYearDto,
  SectionAdmin,
  SettingsMap,
  StaticPageDto,
  SubjectAdmin,
  SystemStatus,
  TagDto,
  UserPatch,
  DashboardDto,
  AdminUserDetail,
} from "./types";

function unwrap<T>(data: unknown, error: unknown, fallback: string): T {
  if (error || !data) throw new Error(apiErrorTitle(error, fallback));
  return data as unknown as T;
}

// ===== Dashboard =====

export function useAdminDashboard() {
  return useQuery({
    queryKey: ["admin", "dashboard"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/dashboard");
      return unwrap<DashboardDto>(data, error, "Không tải được tổng quan");
    },
  });
}

// ===== Người dùng =====

export interface AdminUsersFilters {
  status?: string;
  systemRole?: string;
  teamId?: number;
  q?: string;
  page?: number;
  pageSize?: number;
}

export function useAdminUsersList(filters: AdminUsersFilters) {
  return useQuery({
    queryKey: ["admin", "users", filters],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/users", {
        params: {
          query: {
            status: filters.status || undefined,
            systemRole: filters.systemRole || undefined,
            teamId: filters.teamId,
            q: filters.q || undefined,
            page: filters.page,
            pageSize: filters.pageSize,
          } as never,
        },
      });
      return unwrap<Paged<AdminUserDetail>>(
        data,
        error,
        "Không tải được danh sách người dùng",
      );
    },
  });
}

export function useAdminPatchUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: number; body: UserPatch }) => {
      const { data, error } = await api.PATCH("/api/admin/users/{id}", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      return unwrap<AdminUserDetail>(data, error, "Không cập nhật được");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "users"] });
      qc.invalidateQueries({ queryKey: ["admin", "dashboard"] });
      qc.invalidateQueries({ queryKey: ["me"] });
    },
  });
}

export function useAdminTransferContent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ fromUserId, toUserId }: { fromUserId: number; toUserId: number }) => {
      const { data, error } = await api.POST(
        "/api/admin/users/{id}/transfer-content",
        {
          params: { path: { id: String(fromUserId) } },
          body: { toUserId } as never,
        },
      );
      return unwrap<{ documents: number; quizzes: number }>(
        data,
        error,
        "Không chuyển được nội dung",
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "content"] });
      qc.invalidateQueries({ queryKey: ["admin", "users"] });
    },
  });
}

export function useAdminLogoutAll() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (userId: number) => {
      const { error } = await api.POST("/api/admin/users/{id}/logout-all", {
        params: { path: { id: String(userId) } },
      });
      if (error) throw new Error(apiErrorTitle(error, "Không đăng xuất được"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "users"] });
    },
  });
}

// ===== Nội dung =====

export interface AdminContentFilters {
  kind: "document" | "quiz";
  ownerId?: number;
  teamId?: number;
  sectionId?: number;
  q?: string;
  deleted?: boolean;
  page?: number;
  pageSize?: number;
}

export function useAdminContent(filters: AdminContentFilters) {
  return useQuery({
    queryKey: ["admin", "content", filters],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/content", {
        params: {
          query: {
            kind: filters.kind,
            ownerId: filters.ownerId,
            teamId: filters.teamId,
            sectionId: filters.sectionId,
            q: filters.q || undefined,
            deleted: filters.deleted,
            page: filters.page,
            pageSize: filters.pageSize,
          } as never,
        },
      });
      return unwrap<Paged<AdminContentItem>>(
        data,
        error,
        "Không tải được danh sách nội dung",
      );
    },
  });
}

export function useAdminPatchContent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      kind,
      id,
      body,
    }: {
      kind: "document" | "quiz";
      id: number;
      body: AdminContentPatch;
    }) => {
      const { data, error } = await api.PATCH(
        "/api/admin/content/{kind}/{id}",
        {
          params: { path: { kind, id: String(id) } },
          body: body as never,
        },
      );
      return unwrap<{ id: number; kind: string }>(data, error, "Không lưu được");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "content"] });
      qc.invalidateQueries({ queryKey: ["admin", "dashboard"] });
    },
  });
}

// ===== Báo cáo =====

export function useAdminReports(status?: string) {
  return useQuery({
    queryKey: ["admin", "reports", status],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/reports", {
        params: {
          query: { status: status || undefined } as never,
          page: 1,
          pageSize: 100,
        },
      });
      return unwrap<Paged<AdminReport>>(
        data,
        error,
        "Không tải được báo cáo",
      );
    },
  });
}

export function useAdminPatchReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      body,
    }: {
      id: number;
      body: { status: "Resolved" | "Dismissed"; note?: string };
    }) => {
      const { data, error } = await api.PATCH("/api/admin/reports/{id}", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      return unwrap<{ id: number; status: string }>(
        data,
        error,
        "Không xử lý được báo cáo",
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "reports"] });
      qc.invalidateQueries({ queryKey: ["admin", "dashboard"] });
    },
  });
}

// ===== Danh mục: chuyên mục =====

export function useAdminSections() {
  return useQuery({
    queryKey: ["admin", "sections"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/sections");
      return unwrap<SectionAdmin[]>(data, error, "Không tải được chuyên mục");
    },
  });
}

export function useAdminSaveSection() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: Partial<SectionAdmin> & { name: string }) => {
      const { data, error } = body.id
        ? await api.PUT("/api/admin/sections/{id}", {
            params: { path: { id: String(body.id) } },
            body: body as never,
          })
        : await api.POST("/api/admin/sections", { body: body as never });
      return unwrap<SectionAdmin>(data, error, "Không lưu được chuyên mục");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "sections"] });
    },
  });
}

export function useAdminDeleteSection() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE("/api/admin/sections/{id}", {
        params: { path: { id: String(id) } },
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không tắt được chuyên mục"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "sections"] });
    },
  });
}

// ===== Danh mục: khối =====

export function useAdminGrades() {
  return useQuery({
    queryKey: ["admin", "grades"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/grades");
      return unwrap<GradeAdmin[]>(data, error, "Không tải được khối");
    },
  });
}

export function useAdminSaveGrade() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { name: string; sort?: number }) => {
      const { data, error } = await api.POST("/api/admin/grades", {
        body: body as never,
      });
      return unwrap<GradeAdmin>(data, error, "Không thêm được khối");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "grades"] });
    },
  });
}

export function useAdminUpdateGrade() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, name, sort }: { id: number; name: string; sort?: number }) => {
      const { data, error } = await api.PUT("/api/admin/grades/{id}", {
        params: { path: { id: String(id) } },
        body: { name, sort } as never,
      });
      return unwrap<GradeAdmin>(data, error, "Không sửa được khối");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "grades"] });
    },
  });
}

// ===== Danh mục: môn học =====

export function useAdminSubjects() {
  return useQuery({
    queryKey: ["admin", "subjects"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/subjects");
      return unwrap<SubjectAdmin[]>(data, error, "Không tải được môn học");
    },
  });
}

export function useAdminSaveSubject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: {
      name: string;
      sort?: number;
      isActive?: boolean;
    }) => {
      const { data, error } = await api.POST("/api/admin/subjects", {
        body: body as never,
      });
      return unwrap<SubjectAdmin>(data, error, "Không thêm được môn học");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "subjects"] });
    },
  });
}

export function useAdminDeactivateSubject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE("/api/admin/subjects/{id}", {
        params: { path: { id: String(id) } },
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không tắt được môn học"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "subjects"] });
    },
  });
}

// ===== Danh mục: năm học =====

export function useAdminSchoolYears() {
  return useQuery({
    queryKey: ["admin", "school-years"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/school-years");
      return unwrap<SchoolYearDto[]>(data, error, "Không tải được năm học");
    },
  });
}

export function useAdminCreateSchoolYear() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { name: string; startDate: string; endDate: string }) => {
      const { data, error } = await api.POST("/api/admin/school-years", {
        body: body as never,
      });
      return unwrap<SchoolYearDto>(data, error, "Không tạo được năm học");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "school-years"] });
    },
  });
}

export function useAdminRollover() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      yearId,
      cloneClassesToNextGrade,
    }: {
      yearId: number;
      cloneClassesToNextGrade: boolean;
    }) => {
      const { data, error } = await api.POST(
        "/api/admin/school-years/{id}/rollover",
        {
          params: { path: { id: String(yearId) } },
          body: { cloneClassesToNextGrade } as never,
        },
      );
      return unwrap<RolloverResult>(data, error, "Không kết chuyển được");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "school-years"] });
      qc.invalidateQueries({ queryKey: ["admin", "classes"] });
      qc.invalidateQueries({ queryKey: ["admin", "dashboard"] });
    },
  });
}

// ===== Danh mục: tags =====

export function useAdminTags() {
  return useQuery({
    queryKey: ["admin", "tags"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/tags");
      return unwrap<TagDto[]>(data, error, "Không tải được tags");
    },
  });
}

export function useAdminSaveTag() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (name: string) => {
      const { data, error } = await api.POST("/api/admin/tags", {
        body: { name } as never,
      });
      return unwrap<TagDto>(data, error, "Không thêm được tag");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "tags"] });
    },
  });
}

export function useAdminDeleteTag() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE("/api/admin/tags/{id}", {
        params: { path: { id: String(id) } },
      });
      if (error) throw new Error(apiErrorTitle(error, "Không xóa được tag"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "tags"] });
    },
  });
}

// ===== Lớp =====

export interface AdminClassesFilters {
  schoolYearId?: number;
  gradeId?: number;
  q?: string;
  archived?: boolean;
  page?: number;
  pageSize?: number;
}

export function useAdminClasses(filters: AdminClassesFilters) {
  return useQuery({
    queryKey: ["admin", "classes", filters],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/classes", {
        params: {
          query: {
            schoolYearId: filters.schoolYearId,
            gradeId: filters.gradeId,
            q: filters.q || undefined,
            archived: filters.archived,
            page: filters.page,
            pageSize: filters.pageSize,
          } as never,
        },
      });
      return unwrap<Paged<AdminClass>>(data, error, "Không tải được lớp");
    },
  });
}

export function useAdminChangeHomeroom() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      classId,
      homeroomTeacherId,
    }: {
      classId: number;
      homeroomTeacherId: number;
    }) => {
      const { error } = await api.PATCH("/api/admin/classes/{id}", {
        params: { path: { id: String(classId) } },
        body: { homeroomTeacherId } as never,
      });
      if (error)
        throw new Error(apiErrorTitle(error, "Không đổi được GV chủ nhiệm"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "classes"] });
    },
  });
}

// ===== Thông báo =====

export function useAdminAnnouncements() {
  return useQuery({
    queryKey: ["admin", "announcements"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/announcements");
      return unwrap<AdminAnnouncement[]>(
        data,
        error,
        "Không tải được thông báo",
      );
    },
  });
}

export function useAdminSaveAnnouncement() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: AnnouncementRequest) => {
      const { data, error } = await api.POST("/api/admin/announcements", {
        body: body as never,
      });
      return unwrap<AdminAnnouncement>(data, error, "Không đăng được thông báo");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "announcements"] });
    },
  });
}

export function useAdminUpdateAnnouncement() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: number; body: AnnouncementRequest }) => {
      const { data, error } = await api.PUT("/api/admin/announcements/{id}", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      return unwrap<AdminAnnouncement>(data, error, "Không lưu được thông báo");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "announcements"] });
    },
  });
}

export function useAdminDeleteAnnouncement() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE("/api/admin/announcements/{id}", {
        params: { path: { id: String(id) } },
      });
      if (error) throw new Error(apiErrorTitle(error, "Không xóa được thông báo"));
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "announcements"] });
    },
  });
}

// ===== Trang tĩnh =====

export function useAdminPages() {
  return useQuery({
    queryKey: ["admin", "pages"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/pages");
      return unwrap<StaticPageDto[]>(data, error, "Không tải được trang tĩnh");
    },
  });
}

export function useAdminSavePage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { slug: string; title: string; bodyMarkdown: string }) => {
      const { data, error } = await api.PUT("/api/admin/pages/{slug}", {
        params: { path: { slug: body.slug } },
        body: { title: body.title, bodyMarkdown: body.bodyMarkdown } as never,
      });
      return unwrap<StaticPageDto>(data, error, "Không lưu được trang");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "pages"] });
    },
  });
}

// ===== Cài đặt =====

export function useAdminSettings() {
  return useQuery({
    queryKey: ["admin", "settings"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/settings");
      return unwrap<{ settings: SettingsMap }>(
        data,
        error,
        "Không tải được cài đặt",
      );
    },
  });
}

export function useAdminSaveSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (settings: SettingsMap) => {
      const { data, error } = await api.PUT("/api/admin/settings", {
        body: { settings } as never,
      });
      return unwrap<{ updated: string[] }>(data, error, "Không lưu được cài đặt");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "settings"] });
    },
  });
}

// ===== Nhật ký =====

export interface AdminAuditFilters {
  action?: string;
  actorUserId?: number;
  page?: number;
  pageSize?: number;
}

export function useAdminAuditLogs(filters: AdminAuditFilters) {
  return useQuery({
    queryKey: ["admin", "audit-logs", filters],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/audit-logs", {
        params: {
          query: {
            action: filters.action || undefined,
            actorUserId: filters.actorUserId,
            page: filters.page,
            pageSize: filters.pageSize,
          } as never,
        },
      });
      return unwrap<Paged<AdminAuditLog>>(
        data,
        error,
        "Không tải được nhật ký",
      );
    },
  });
}

// ===== Hệ thống =====

export function useAdminSystem() {
  return useQuery({
    queryKey: ["admin", "system"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/admin/system");
      return unwrap<SystemStatus>(data, error, "Không tải được trạng thái hệ thống");
    },
  });
}

export function useAdminRetryFile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (fileId: number) => {
      const { data, error } = await api.POST("/api/admin/system/files/{id}/retry", {
        params: { path: { id: String(fileId) } },
      });
      return unwrap<{ id: number; status: string }>(
        data,
        error,
        "Không chạy lại được",
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "system"] });
    },
  });
}

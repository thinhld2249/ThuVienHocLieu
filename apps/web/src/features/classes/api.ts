import { useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import { apiErrorTitle } from "@/features/auth/api";
import { useMyQuizzes } from "@/features/quizzes/api";
import { usePublicItems } from "@/features/documents/api";
import type {
  AssignmentDto,
  ClassDto,
  CreateAssignmentRequest,
  CreateAssignmentAttemptRequest,
  CreateClassRequest,
  CreateStudentRequest,
  GradebookDto,
  PublicAssignmentDto,
  StudentDto,
  StudentImportResult,
  UpdateAssignmentRequest,
  UpdateClassRequest,
  UpdateStudentRequest,
} from "./types";

function unwrap<T>(data: unknown, error: unknown, fallback: string): T {
  if (error || !data)
    throw Object.assign(new Error(apiErrorTitle(error, fallback)), {
      data: error,
    });
  return data as unknown as T;
}

// ===== Lớp (spec §7, §10) =====

export function useMyClasses() {
  return useQuery({
    queryKey: ["teacher", "classes"],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teacher/classes");
      return unwrap<ClassDto[]>(data, error, "Không tải được danh sách lớp");
    },
  });
}

export function useClass(id: number | null) {
  return useQuery({
    queryKey: ["teacher", "class", id],
    enabled: id != null,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/teacher/classes/{id}", {
        params: { path: { id: String(id) } },
      });
      return unwrap<ClassDto>(data, error, "Không tìm thấy lớp");
    },
  });
}

function invalidateClass(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ["teacher", "classes"] });
  qc.invalidateQueries({ queryKey: ["teacher", "class"] });
}

export function useCreateClass() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateClassRequest) => {
      const { data, error } = await api.POST("/api/teacher/classes", {
        body: body as never,
      });
      return unwrap<ClassDto>(data, error, "Không tạo được lớp");
    },
    onSuccess: () => invalidateClass(qc),
  });
}

export interface UpdateClassVars {
  id: number;
  body: UpdateClassRequest;
}

export function useUpdateClass() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: UpdateClassVars) => {
      const { data, error } = await api.PUT("/api/teacher/classes/{id}", {
        params: { path: { id: String(id) } },
        body: body as never,
      });
      if (error || !data)
        throw Object.assign(
          new Error(apiErrorTitle(error, "Không lưu được lớp")),
          { data: error },
        );
      return data as unknown as ClassDto;
    },
    onSuccess: () => invalidateClass(qc),
  });
}

export function useDeleteClass() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE("/api/teacher/classes/{id}", {
        params: { path: { id: String(id) } },
      });
      if (error) throw new Error(apiErrorTitle(error, "Không xóa được lớp"));
    },
    onSuccess: () => invalidateClass(qc),
  });
}

// ===== Học sinh (spec §7) =====

export function useStudents(classId: number | null) {
  return useQuery({
    queryKey: ["teacher", "class", classId, "students"],
    enabled: classId != null,
    queryFn: async () => {
      const { data, error } = await api.GET(
        "/api/teacher/classes/{id}/students",
        { params: { path: { id: String(classId) } } },
      );
      return unwrap<StudentDto[]>(
        data,
        error,
        "Không tải được danh sách học sinh",
      );
    },
  });
}

function invalidateStudents(
  qc: ReturnType<typeof useQueryClient>,
  classId: number,
) {
  qc.invalidateQueries({
    queryKey: ["teacher", "class", classId, "students"],
  });
  qc.invalidateQueries({ queryKey: ["teacher", "class"] });
  invalidateClass(qc);
}

export function useCreateStudent(classId: number | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateStudentRequest) => {
      const { data, error } = await api.POST(
        "/api/teacher/classes/{id}/students",
        {
          params: { path: { id: String(classId) } },
          body: body as never,
        },
      );
      if (error || !data)
        throw Object.assign(
          new Error(apiErrorTitle(error, "Không thêm được học sinh")),
          { data: error },
        );
      return data as unknown as StudentDto;
    },
    onSuccess: () => {
      if (classId != null) invalidateStudents(qc, classId);
    },
  });
}

export interface UpdateStudentVars {
  classId: number;
  studentId: number;
  body: UpdateStudentRequest;
}

export function useUpdateStudent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ classId, studentId, body }: UpdateStudentVars) => {
      const { data, error } = await api.PUT(
        "/api/teacher/classes/{id}/students/{studentId}",
        {
          params: {
            path: { id: String(classId), studentId: String(studentId) },
          },
          body: body as never,
        },
      );
      if (error || !data)
        throw Object.assign(
          new Error(apiErrorTitle(error, "Không lưu được học sinh")),
          { data: error },
        );
      return data as unknown as StudentDto;
    },
    onSuccess: (_d, vars) => invalidateStudents(qc, vars.classId),
  });
}

export function useDeleteStudent(classId: number | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (studentId: number) => {
      const { error } = await api.DELETE(
        "/api/teacher/classes/{id}/students/{studentId}",
        {
          params: {
            path: { id: String(classId), studentId: String(studentId) },
          },
        },
      );
      if (error)
        throw new Error(apiErrorTitle(error, "Không xóa được học sinh"));
    },
    onSuccess: () => {
      if (classId != null) invalidateStudents(qc, classId);
    },
  });
}

/**
 * §7: nhập danh sách HS từ Excel (multipart, XHR vì cần header CSRF).
 * dryRun=true → chỉ báo cáo, không ghi.
 */
export function importStudents(
  classId: number,
  file: File,
  dryRun: boolean,
): Promise<StudentImportResult> {
  const url = `/api/teacher/classes/${classId}/students/import?dryRun=${dryRun}`;
  const form = new FormData();
  form.append("file", file);

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
        resolve(body as StudentImportResult);
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

export function studentsExportUrl(classId: number): string {
  return `/api/teacher/classes/${classId}/students/export.xlsx`;
}

// ===== Bảng điểm (spec §7) =====

export function useGradebook(classId: number | null) {
  return useQuery({
    queryKey: ["teacher", "class", classId, "gradebook"],
    enabled: classId != null,
    queryFn: async () => {
      const { data, error } = await api.GET(
        "/api/teacher/classes/{id}/gradebook",
        { params: { path: { id: String(classId) } } },
      );
      return unwrap<GradebookDto>(data, error, "Không tải được bảng điểm");
    },
  });
}

export function gradebookExportUrl(classId: number): string {
  return `/api/teacher/classes/${classId}/gradebook.xlsx`;
}

// ===== Giao bài (spec §6.6, §7) =====

export function useClassAssignments(classId: number | null) {
  return useQuery({
    queryKey: ["teacher", "class", classId, "assignments"],
    enabled: classId != null,
    queryFn: async () => {
      const { data, error } = await api.GET(
        "/api/teacher/classes/{id}/assignments",
        { params: { path: { id: String(classId) } } },
      );
      return unwrap<AssignmentDto[]>(
        data,
        error,
        "Không tải được danh sách bài đã giao",
      );
    },
  });
}

function invalidateAssignments(
  qc: ReturnType<typeof useQueryClient>,
  classId: number,
) {
  qc.invalidateQueries({
    queryKey: ["teacher", "class", classId, "assignments"],
  });
  invalidateStudents(qc, classId);
}

export function useCreateAssignment(classId: number | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateAssignmentRequest) => {
      const { data, error } = await api.POST(
        "/api/teacher/classes/{id}/assignments",
        {
          params: { path: { id: String(classId) } },
          body: body as never,
        },
      );
      return unwrap<AssignmentDto>(data, error, "Không giao được bài");
    },
    onSuccess: () => {
      if (classId != null) invalidateAssignments(qc, classId);
    },
  });
}

export interface UpdateAssignmentVars {
  classId: number;
  assignmentId: number;
  body: UpdateAssignmentRequest;
}

export function useUpdateAssignment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      classId,
      assignmentId,
      body,
    }: UpdateAssignmentVars) => {
      const { data, error } = await api.PUT(
        "/api/teacher/classes/{id}/assignments/{assignmentId}",
        {
          params: {
            path: {
              id: String(classId),
              assignmentId: String(assignmentId),
            },
          },
          body: body as never,
        },
      );
      if (error || !data)
        throw Object.assign(
          new Error(apiErrorTitle(error, "Không cập nhật được bài giao")),
          { data: error },
        );
      return data as unknown as AssignmentDto;
    },
    onSuccess: (_d, vars) => invalidateAssignments(qc, vars.classId),
  });
}

export function useDeleteAssignment(classId: number | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (assignmentId: number) => {
      const { error } = await api.DELETE(
        "/api/teacher/classes/{id}/assignments/{assignmentId}",
        {
          params: {
            path: { id: String(classId), assignmentId: String(assignmentId) },
          },
        },
      );
      if (error)
        throw new Error(apiErrorTitle(error, "Không xóa được bài giao"));
    },
    onSuccess: () => {
      if (classId != null) invalidateAssignments(qc, classId);
    },
  });
}

// ===== Khu công khai — mã giao bài (spec §6.6, §7, §10) =====

export function usePublicAssignment(code: string | null) {
  return useQuery({
    queryKey: ["public", "assignment", code],
    enabled: code != null && code.length > 0,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/public/assignments/{code}", {
        params: { path: { code: String(code) } },
      });
      return unwrap<PublicAssignmentDto>(
        data,
        error,
        "Không tìm thấy bài tập với mã này",
      );
    },
  });
}

export function useCreateAssignmentAttempt(code: string | null) {
  return useMutation({
    mutationFn: async (body: CreateAssignmentAttemptRequest) => {
      const { data, error } = await api.POST(
        "/api/public/assignments/{code}/attempts",
        {
          params: { path: { code: String(code) } },
          body: body as never,
        },
      );
      if (error || !data)
        throw Object.assign(
          new Error(
            apiErrorTitle(
              error,
              "Không bắt đầu được bài làm (đã hết lượt hoặc bài đã đóng)",
            ),
          ),
          { data: error },
        );
      return data as unknown as { id: string };
    },
  });
}

// ===== Bộ lọc chọn bài tập để giao (của mình hoặc Public của đồng nghiệp — spec §7) =====

export interface AssignableQuiz {
  id: number;
  title: string;
  owner: "me" | "public";
}

/**
 * Quiz được phép giao = quiz của tôi (mọi trạng thái) + quiz Public đang hiện của đồng nghiệp.
 * BE kiểm tra quyền thật; danh sách này chỉ là bộ lọc UI.
 */
export function useAssignableQuizzes(): {
  quizzes: AssignableQuiz[];
  isPending: boolean;
} {
  const mine = useMyQuizzes({ page: 1, pageSize: 100 });
  const pub = usePublicItems({ kind: "quiz", page: 1, pageSize: 100 });
  const quizzes = useMemo(() => {
    const map = new Map<number, AssignableQuiz>();
    for (const r of pub.data?.items ?? [])
      if (r.kind === "quiz")
        map.set(r.id, { id: r.id, title: r.title, owner: "public" });
    for (const r of mine.data?.items ?? [])
      map.set(r.id, { id: r.id, title: r.title, owner: "me" });
    return [...map.values()].sort((a, b) =>
      a.title.localeCompare(b.title, "vi"),
    );
  }, [mine.data, pub.data]);
  return { quizzes, isPending: mine.isPending || pub.isPending };
}

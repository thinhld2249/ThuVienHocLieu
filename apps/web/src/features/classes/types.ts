// Gương DTO backend (Features/Classes/ClassDtos.cs, Features/Assignments/AssignmentDtos.cs).
// Sinh tay theo quy ước dự án: generator chỉ xuất path/params, không xuất schema (spec §8.5).

export interface ClassTeacherDto {
  userId: number;
  fullName: string;
  subjectId: number | null;
  subjectName: string | null;
}

/** 1 hàng trong bảng /gv/lop. */
export interface ClassDto {
  id: number;
  name: string;
  gradeId: number | null;
  gradeName: string | null;
  schoolYearId: number | null;
  schoolYearName: string | null;
  teamId: number | null;
  teamName: string | null;
  homeroomTeacherId: number | null;
  homeroomTeacherName: string | null;
  teachers: ClassTeacherDto[];
  studentCount: number;
  assignmentCount: number;
  isArchived: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ClassTeacherItem {
  userId: number;
  subjectId: number | null;
}

export interface CreateClassRequest {
  name: string;
  gradeId?: number | null;
  schoolYearId?: number | null;
  teamId?: number | null;
  teachers?: ClassTeacherItem[] | null;
}

export interface UpdateClassRequest {
  name?: string | null;
  gradeId?: number | null;
  schoolYearId?: number | null;
  teamId?: number | null;
  teachers?: ClassTeacherItem[] | null;
}

export interface StudentDto {
  id: number;
  ordinal: number | null;
  fullName: string;
  studentCode: string | null;
  /** "yyyy-MM-dd" (DateOnly). */
  dateOfBirth: string | null;
  gender: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateStudentRequest {
  fullName: string;
  studentCode?: string | null;
  /** "yyyy-MM-dd". */
  dateOfBirth?: string | null;
  gender?: string | null;
}

export interface UpdateStudentRequest {
  fullName?: string | null;
  studentCode?: string | null;
  dateOfBirth?: string | null;
  gender?: string | null;
  ordinal?: number | null;
  isActive?: boolean | null;
}

/** §7: kết quả nhập danh sách HS từ Excel (row = dòng trong file, bắt đầu từ 2). */
export interface StudentImportRowError {
  row: number;
  message: string;
}

export interface StudentImportResult {
  dryRun: boolean;
  totalRows: number;
  importCount: number;
  duplicateCount: number;
  errors: StudentImportRowError[];
}

/** 1 bài đã giao trong tab "Bài đã giao" (spec §7). */
export interface AssignmentDto {
  id: number;
  quizId: number;
  quizTitle: string;
  code: string;
  useRoster: boolean;
  openAt: string | null;
  closeAt: string | null;
  attemptCount: number;
  bestScore10: number | null;
  creatorName: string;
  createdAt: string;
}

export interface CreateAssignmentRequest {
  quizId: number;
  useRoster: boolean;
  openAt?: string | null;
  closeAt?: string | null;
}

/** null = giữ nguyên giá trị hiện có (BE M5). */
export interface UpdateAssignmentRequest {
  useRoster?: boolean | null;
  openAt?: string | null;
  closeAt?: string | null;
}

export interface GradebookStudentRow {
  studentId: number;
  fullName: string;
}

export interface GradebookAssignmentColumn {
  assignmentId: number;
  quizTitle: string;
  code: string;
  createdAt: string;
}

/** Hàng = học sinh, cột = bài đã giao, ô = điểm cao nhất (null = chưa làm). */
export interface GradebookDto {
  className: string;
  columns: GradebookAssignmentColumn[];
  students: GradebookStudentRow[];
  scores: (number | null)[][];
}

// ===== Khu công khai — mã giao bài (spec §6.6, §7) =====

export interface PublicRosterStudentDto {
  id: number;
  fullName: string;
}

/** Status: "Scheduled" (chưa tới giờ mở) · "Open" · "Closed". */
export interface PublicAssignmentDto {
  code: string;
  status: "Scheduled" | "Open" | "Closed";
  quizTitle: string;
  questionCount: number;
  timeLimitMinutes: number | null;
  identityMode: string;
  maxAttempts: number | null;
  usedAttempts: number;
  openAt: string | null;
  closeAt: string | null;
  className: string;
  /** Chỉ trả về khi useRoster VÀ đang mở (không lộ danh sách lớp — spec §7). */
  roster: PublicRosterStudentDto[] | null;
}

export interface CreateAssignmentAttemptRequest {
  studentId?: number | null;
  guestName?: string | null;
}

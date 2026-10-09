// DTO bài tập (M4) — khớp QuizDtos.cs + AttemptDtos.cs phía BE.
// Sinh tay theo quy ước dự án: generator chỉ xuất path/params, không xuất schema.

export type QuestionType = "Single" | "Multi" | "TrueFalse";
export type ShowAnswers = "Never" | "AfterSubmit" | "AfterClose";
export type IdentityMode = "Anonymous" | "Name" | "NameAndClass";
export type MultiScoring = "AllOrNothing" | "Partial";
export type ScoreRounding = "None" | "Quarter" | "Half" | "Integer";
export type Scope = "Public" | "Teachers" | "Team" | "Private";

/** Cài đặt làm bài (spec §6.5 tab Cài đặt). undefined/null = giữ mặc định. */
export interface QuizSettings {
  timeLimitMinutes?: number | null;
  shuffleQuestions?: boolean | null;
  shuffleOptions?: boolean | null;
  showAnswers?: ShowAnswers | null;
  identityMode?: IdentityMode | null;
  maxAttempts?: number | null;
  multiScoring?: MultiScoring | null;
  scoreRounding?: ScoreRounding | null;
  /** true = chỉ học sinh của lớp đã được giao bài (qua mã/QR) xem & làm được. */
  classOnly?: boolean | null;
}

export interface QuizOptionInput {
  id?: number | null;
  sort?: number | null;
  contentHtml?: string | null;
  isCorrect?: boolean | null;
}

export interface QuizQuestionInput {
  id?: number | null;
  sort?: number | null;
  groupId?: number | null;
  type?: string | null;
  contentHtml?: string | null;
  explanationHtml?: string | null;
  points?: number | null;
  options?: QuizOptionInput[] | null;
}

export interface QuizGroupInput {
  id?: number | null;
  sort?: number | null;
  title?: string | null;
  passageHtml?: string | null;
}

export interface UpdateQuizRequest {
  title?: string | null;
  sectionId?: number | null;
  gradeId?: number | null;
  subjectId?: number | null;
  schoolYearId?: number | null;
  weekNo?: number | null;
  scope?: string | null;
  teamId?: number | null;
  descriptionHtml?: string | null;
  printFileId?: number | null;
  settings?: QuizSettings | null;
  groups?: QuizGroupInput[] | null;
  questions?: QuizQuestionInput[] | null;
  updatedAt?: string | null;
}

export type CreateQuizRequest = Omit<UpdateQuizRequest, "updatedAt">;

/** 1 hàng trong bảng /gv/bai-tap (kể cả ẩn). */
export interface MyQuizRow {
  id: number;
  title: string;
  slug: string;
  sectionSlug: string | null;
  sectionName: string | null;
  grade: number | null;
  subjectName: string | null;
  schoolYearName: string | null;
  weekNo: number | null;
  scope: string;
  publishMode: string;
  publishFrom: string | null;
  publishUntil: string | null;
  publishState: string;
  moderationStatus: string;
  questionCount: number;
  totalPoints: number;
  attemptCount: number;
  hasImportWarnings: boolean;
  createdAt: string;
  updatedAt: string;
  isDeleted: boolean;
}

export interface QuizGroupDto {
  id: number;
  sort: number;
  title: string | null;
  passageHtml: string | null;
}

export interface QuizOptionDto {
  id: number;
  sort: number;
  contentHtml: string;
  isCorrect: boolean;
}

export interface QuizQuestionDto {
  id: number;
  sort: number;
  groupId: number | null;
  type: string;
  contentHtml: string;
  explanationHtml: string | null;
  points: number;
  options: QuizOptionDto[];
}

/** Chi tiết cho tabs của /gv/bai-tap/:id. */
export interface QuizDetail {
  id: number;
  title: string;
  slug: string;
  descriptionHtml: string | null;
  sectionId: number | null;
  sectionSlug: string | null;
  sectionName: string | null;
  gradeId: number | null;
  gradeName: string | null;
  subjectId: number | null;
  subjectName: string | null;
  schoolYearId: number | null;
  schoolYearName: string | null;
  weekNo: number | null;
  teamId: number | null;
  teamName: string | null;
  ownerId: number | null;
  ownerName: string | null;
  scope: string;
  /** true = chỉ học sinh của lớp đã được giao bài (qua mã/QR) xem & làm được. */
  classOnly: boolean;
  publishMode: string;
  publishFrom: string | null;
  publishUntil: string | null;
  publishState: string;
  moderationStatus: string;
  moderationNote: string | null;
  timeLimitMinutes: number | null;
  shuffleQuestions: boolean;
  shuffleOptions: boolean;
  showAnswers: string;
  identityMode: string;
  maxAttempts: number | null;
  multiScoring: string;
  scoreRounding: string;
  printFileId: number | null;
  /** jsonb serialize — null (chưa import) hoặc "[]" / "[{...}]" (import). */
  importWarnings: string | null;
  questionCount: number;
  totalPoints: number;
  attemptCount: number;
  groups: QuizGroupDto[];
  questions: QuizQuestionDto[];
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

/** Cảnh báo khi import (spec §6.3). */
export interface QuizWarning {
  code: string;
  questionNumber: number | null;
  message: string;
}

export interface QuizImportResult {
  quizId: number;
  warnings: QuizWarning[];
}

// ===== Kết quả & thống kê (spec §6.8) =====

/** 1 lượt làm trong bảng kết quả. */
export interface AttemptRow {
  id: string;
  studentName: string | null;
  studentClass: string | null;
  guestName: string | null;
  status: string;
  score10: number | null;
  correctCount: number | null;
  questionCount: number | null;
  durationSec: number | null;
  startedAt: string;
  submittedAt: string | null;
  attemptNo: number;
}

export interface ScoreBucket {
  score10: number;
  count: number;
}

export interface OptionPickRow {
  optionId: number;
  preview: string;
  picks: number;
  isCorrect: boolean;
}

export interface QuestionStatRow {
  id: number;
  number: number;
  preview: string;
  attempted: number;
  correctCount: number;
  correctPercent: number;
  optionPicks: OptionPickRow[];
}

export interface QuizStats {
  attemptCount: number;
  averageScore10: number | null;
  medianScore10: number | null;
  minScore10: number | null;
  maxScore10: number | null;
  distribution: ScoreBucket[];
  questions: QuestionStatRow[];
}

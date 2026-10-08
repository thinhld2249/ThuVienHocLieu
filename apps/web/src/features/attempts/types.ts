// DTO làm bài công khai (M4) — khớp AttemptDtos.cs phía BE.
// Quy tắc an toàn (spec §12): không có isCorrect/explanation trước khi show_answers cho phép.

/** Giới thiệu quiz trước khi làm bài (/bai-tap/:slug-:id). */
export interface PublicQuizDetail {
  id: number
  slug: string
  title: string
  descriptionHtml: string | null
  sectionSlug: string | null
  sectionName: string | null
  grade: number | null
  subjectName: string | null
  weekNo: number | null
  schoolYearName: string | null
  questionCount: number
  timeLimitMinutes: number | null
  publishState: string
  publishFrom: string | null
  publishUntil: string | null
  identityMode: string
  maxAttempts: number | null
  attemptCount: number
  printFileId: number | null
  /** Lượt đang dở trên thiết bị này (cookie hl_dev) → nút "Làm tiếp". */
  inProgressAttemptId: string | null
  createdAt: string
}

export interface AttemptOption {
  id: number
  contentHtml: string
}

export interface AttemptQuestion {
  id: number
  groupId: number | null
  number: number
  type: "Single" | "Multi" | "TrueFalse"
  contentHtml: string
  options: AttemptOption[]
}

export interface AttemptGroup {
  id: number
  title: string | null
  passageHtml: string | null
}

export interface SavedAnswer {
  questionId: number
  optionIds: number[]
}

export interface AttemptReviewOption {
  id: number
  contentHtml: string
  selected: boolean
  isCorrect: boolean
}

export interface AttemptReviewQuestion {
  id: number
  number: number
  contentHtml: string
  options: AttemptReviewOption[]
  explanationHtml: string | null
}

/** null khi status = InProgress hoặc show_answers chưa cho phép xem. */
export interface AttemptResult {
  score10: number
  score: number
  totalPoints: number
  correctCount: number | null
  questionCount: number | null
  durationSec: number | null
  submittedAt: string
  canReview: boolean
  review: AttemptReviewQuestion[] | null
}

export type AttemptStatus = "InProgress" | "Submitted" | "Expired"

/** POST create / GET attempt: InProgress (câu + đã lưu) hoặc đã nộp (kết quả). */
export interface AttemptDto {
  id: string
  status: AttemptStatus
  expiresAt: string | null
  title: string
  timeLimitMinutes: number | null
  questionCount: number
  identityMode: string
  maxAttempts: number | null
  usedAttempts: number
  guestName: string
  guestClass: string
  groups: AttemptGroup[]
  questions: AttemptQuestion[]
  answers: SavedAnswer[]
  result: AttemptResult | null
}

/** Danh sách attempt đang dở trên thiết bị (localStorage hl_attempts). */
export interface LocalAttemptRef {
  attemptId: string
  quizId: number
  quizSlug: string
  quizTitle: string
  startedAt: string
}

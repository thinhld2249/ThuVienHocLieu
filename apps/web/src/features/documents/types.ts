// DTO khu tài liệu (M3) — khớp DocumentDtos.cs + FileDtos.cs phía BE.
// Sinh tay theo quy ước dự án: generator chỉ xuất path/params, không xuất schema.

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

/** 1 thẻ trong lưới /api/public/items (tài liệu + bài tập gộp). */
export interface PublicItemRow {
  kind: 'document' | 'quiz'
  id: number
  slug: string
  title: string
  summary: string | null
  sectionSlug: string | null
  sectionName: string | null
  grade: number | null
  subjectName: string | null
  weekNo: number | null
  schoolYearName: string | null
  questionCount: number
  timeLimitMinutes: number | null
  publishState: string
  publishUntil: string | null
  viewCount: number
  createdAt: string
}

export interface AuthorDto {
  fullName: string
  teamName: string | null
}

export interface PublicFileDto {
  id: number
  name: string
  size: number
  ext: string
  pages: number | null
  ready: boolean
}

/** Chi tiết tài liệu công khai (spec §5.1). */
export interface PublicDocumentDetail {
  id: number
  slug: string
  title: string
  summary: string | null
  descriptionHtml: string | null
  sectionSlug: string | null
  sectionName: string | null
  grade: number | null
  subjectName: string | null
  weekNo: number | null
  schoolYearName: string | null
  author: AuthorDto | null
  files: PublicFileDto[]
  allowGuestDownload: boolean
  viewCount: number
  createdAt: string
}

/** 1 hàng trong bảng /gv/tai-lieu (kể cả ẩn). */
export interface MyDocumentRow {
  id: number
  title: string
  slug: string
  sectionSlug: string | null
  sectionName: string | null
  grade: number | null
  subjectName: string | null
  schoolYearName: string | null
  weekNo: number | null
  scope: string
  publishMode: string
  publishFrom: string | null
  publishUntil: string | null
  publishState: string
  moderationStatus: string
  fileCount: number
  viewCount: number
  downloadCount: number
  createdAt: string
  updatedAt: string
  isDeleted: boolean
}

/** Chi tiết tài liệu cho GV chủ sở hữu (form sửa). */
export interface DocumentDetail {
  id: number
  title: string
  slug: string
  summary: string | null
  descriptionHtml: string | null
  sectionId: number | null
  sectionSlug: string | null
  sectionName: string | null
  gradeId: number | null
  gradeName: string | null
  subjectId: number | null
  subjectName: string | null
  schoolYearId: number | null
  schoolYearName: string | null
  weekNo: number | null
  teamId: number | null
  teamName: string | null
  ownerId: number | null
  ownerName: string | null
  scope: string
  publishMode: string
  publishFrom: string | null
  publishUntil: string | null
  publishState: string
  moderationStatus: string
  moderationNote: string | null
  allowGuestDownload: boolean
  coverFileId: number | null
  files: FileDto[]
  tags: TagRef[]
  isDeleted: boolean
  createdAt: string
  updatedAt: string
  viewCount: number
  downloadCount: number
}

/** File đã upload (trước/khi tạo tài liệu). */
export interface FileDto {
  id: number
  originalName: string
  ext: string
  bytes: number
  processingStatus: 'Pending' | 'Processing' | 'Ready' | 'Failed' | 'NotApplicable'
  processingError: string | null
  previewPages: number | null
  createdAt: string
}

export interface FilePageDto {
  number: number
  url: string
  kind: 'pdf' | 'image' | 'video'
}

export interface FilePagesDto {
  fileId: number
  pages: FilePageDto[]
}

export interface TagRef {
  id: number
  name: string
}

// ===== Form tạo/sửa — null/undefined = giữ mặc định =====

export interface CreateDocumentRequest {
  title?: string | null
  sectionId?: number | null
  gradeId?: number | null
  subjectId?: number | null
  schoolYearId?: number | null
  weekNo?: number | null
  scope?: string | null
  teamId?: number | null
  summary?: string | null
  descriptionHtml?: string | null
  fileIds?: number[] | null
  coverFileId?: number | null
  allowGuestDownload?: boolean | null
  publishMode?: string | null
  publishFrom?: string | null
  publishUntil?: string | null
}

export type UpdateDocumentRequest = CreateDocumentRequest & {
  updatedAt?: string | null
}

export type PublishMode = 'Visible' | 'Hidden' | 'Scheduled'

export interface PublishRequest {
  mode: PublishMode
  from?: string | null
  until?: string | null
}

export interface BulkPublishResult {
  applied: number
  failed: { kind: string; id: number; code: string }[]
}

/** Yêu thích (GET /api/teacher/favorites). */
export interface FavoriteRow {
  kind: 'document' | 'quiz'
  itemId: number
  title: string
  slug: string
  sectionName: string | null
  grade: number | null
  addedAt: string
}

/** Lỗi validation 422: { title, code, errors: { <camelKey>: string[] } } */
export interface ApiValidation {
  title?: string
  code?: string
  errors?: Record<string, string[]>
}

export function validationErrors(err: unknown): Record<string, string[]> {
  const e = err as { data?: ApiValidation } | undefined
  return e?.data?.errors ?? {}
}

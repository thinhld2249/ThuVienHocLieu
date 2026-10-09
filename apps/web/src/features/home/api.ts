import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api-client";

export interface SectionDto {
  id: number;
  slug: string;
  name: string;
  icon: string | null;
  color: string | null;
  sort: number;
  contentKind: string;
  requireWeek: boolean;
  isInternal: boolean;
}

export interface GradeDto {
  id: number;
  name: string;
  sort: number;
}

export interface SubjectDto {
  id: number;
  name: string;
  slug: string;
  sort: number;
}

export interface SchoolYearDto {
  id: number;
  name: string;
  isCurrent: boolean;
  startDate: string;
  endDate: string;
}

export interface TaxonomyDto {
  sections: SectionDto[];
  grades: GradeDto[];
  subjects: SubjectDto[];
  schoolYears: SchoolYearDto[];
}

export interface HomeOpenQuizDto {
  id: number;
  slug: string;
  title: string;
  subjectName: string | null;
  grade: number | null;
  weekLabel: string | null;
  untilAt: string | null;
}

export interface HomeItemDto {
  kind: "document" | "quiz";
  id: number;
  slug: string;
  title: string;
  summary: string | null;
  sectionSlug: string | null;
  sectionName: string | null;
  grade: number | null;
  subjectName: string | null;
  createdAt: string;
}

export interface HomeDto {
  openQuizzes: HomeOpenQuizDto[];
  recent: HomeItemDto[];
  featured: HomeItemDto[];
  sections: SectionDto[];
  announcements: {
    id: number;
    title: string;
    bodyHtml: string;
    isPinned: boolean;
    createdAt: string;
  }[];
  stats: { documents: number; quizzes: number; teachers: number };
}

export function useTaxonomy() {
  return useQuery({
    queryKey: ["public", "taxonomy"],
    // Danh mục thay đổi hiếm (Admin sửa) — cache 30s như BE.
    staleTime: 30_000,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/public/taxonomy");
      if (error || !data) throw new Error("Không tải được danh mục");
      return data as unknown as TaxonomyDto;
    },
  });
}

export function useHome(grade: number | null) {
  return useQuery({
    queryKey: ["public", "home", grade],
    staleTime: 30_000,
    queryFn: async () => {
      const { data, error } = await api.GET("/api/public/home", {
        params: { query: grade ? { grade } : {} },
      });
      if (error || !data) throw new Error("Không tải được trang chủ");
      return data as unknown as HomeDto;
    },
  });
}

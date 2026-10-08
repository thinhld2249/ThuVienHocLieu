import { FileText } from "lucide-react";
import type { PublicItemRow } from "@/features/documents/types";
import { useTaxonomy } from "@/features/home/api";
import { DocumentCard, QuizCard } from "@/components/common/ContentCard";
import { EmptyState } from "@/components/common/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * Lưới thẻ tài liệu + bài tập (spec §14.3): 4 cột desktop, 2 cột mobile.
 * Màu dải thẻ lấy từ màu chuyên mục trong taxonomy.
 */
export function ItemGrid({
  items,
  loading,
  emptyTitle = "Chưa có nội dung",
  emptyDescription,
  emptyAction,
}: {
  items: PublicItemRow[];
  loading?: boolean;
  emptyTitle?: string;
  emptyDescription?: string;
  emptyAction?: React.ReactNode;
}) {
  const { data: taxonomy } = useTaxonomy();
  const sectionColor = (slug: string | null) =>
    slug
      ? (taxonomy?.sections.find((s) => s.slug === slug)?.color ?? null)
      : null;

  if (loading)
    return (
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-4">
        {Array.from({ length: 8 }, (_, i) => (
          <Skeleton key={i} className="aspect-[3/4] rounded-card" />
        ))}
      </div>
    );

  if (items.length === 0)
    return (
      <EmptyState
        icon={FileText}
        title={emptyTitle}
        description={emptyDescription}
        action={emptyAction}
      />
    );

  return (
    <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-4">
      {items.map((item) =>
        item.kind === "quiz" ? (
          <QuizCard
            key={`${item.kind}-${item.id}`}
            item={{
              slug: item.slug,
              id: item.id,
              title: item.title,
              grade: item.grade,
              subjectName: item.subjectName,
            }}
            questionCount={item.questionCount}
            timeLimitMinutes={item.timeLimitMinutes}
            untilAt={item.publishUntil}
            sectionColor={sectionColor(item.sectionSlug)}
          />
        ) : (
          <DocumentCard
            key={`document-${item.id}`}
            item={{
              slug: item.slug,
              id: item.id,
              title: item.title,
              sectionName: item.sectionName,
              grade: item.grade,
              subjectName: item.subjectName,
            }}
            sectionColor={sectionColor(item.sectionSlug)}
          />
        ),
      )}
    </div>
  );
}

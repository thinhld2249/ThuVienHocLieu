import { Link } from "react-router";
import { Clock3, FileText } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { formatDateTime, formatTime } from "@/lib/date";
import { contentUrl } from "@/lib/slug";

/**
 * Thẻ nội dung (spec §14.3): ảnh trang 1 tỉ lệ A4 + dải màu chuyên mục + tiêu đề 2 dòng + nhãn.
 * Thẻ quiz: khối thông tin (số câu, thời gian, "Đang mở đến …") thay cho ảnh.
 */
export function DocumentCard({
  item,
  coverUrl,
  sectionColor,
}: {
  item: {
    slug: string;
    id: number;
    title: string;
    sectionName?: string | null;
    grade?: number | null;
    subjectName?: string | null;
  };
  coverUrl?: string | null;
  sectionColor?: string | null;
}) {
  return (
    <Link
      to={contentUrl("tai-lieu", item.slug, item.id)}
      className="group relative flex flex-col overflow-hidden rounded-card border border-grid bg-white transition hover:border-violet/40"
    >
      <span
        aria-hidden
        className="absolute inset-y-0 left-0 z-10 w-1"
        style={{ backgroundColor: sectionColor ?? "var(--color-violet)" }}
      />
      <div className="relative aspect-[1/1.414] w-full overflow-hidden bg-grid/30">
        {coverUrl ? (
          <img
            src={coverUrl}
            alt=""
            loading="lazy"
            className="h-full w-full object-cover object-top"
          />
        ) : (
          <div className="flex h-full items-center justify-center text-muted/40">
            <FileText className="size-10" aria-hidden />
          </div>
        )}
      </div>
      <div className="flex flex-1 flex-col gap-2 p-3">
        <h3 className="line-clamp-2 text-sm font-medium leading-snug group-hover:text-violet">
          {item.title}
        </h3>
        <div className="mt-auto flex flex-wrap gap-1.5">
          {item.grade != null ? (
            <Badge variant="outline">Khối {item.grade}</Badge>
          ) : null}
          {item.subjectName ? (
            <Badge variant="outline">{item.subjectName}</Badge>
          ) : null}
        </div>
      </div>
    </Link>
  );
}

export function QuizCard({
  item,
  questionCount,
  timeLimitMinutes,
  untilAt,
  sectionColor,
}: {
  item: {
    slug: string;
    id: number;
    title: string;
    grade?: number | null;
    subjectName?: string | null;
  };
  questionCount?: number;
  timeLimitMinutes?: number | null;
  untilAt?: string | null;
  sectionColor?: string | null;
}) {
  return (
    <Link
      to={contentUrl("bai-tap", item.slug, item.id)}
      className="group flex flex-col overflow-hidden rounded-card border border-grid bg-white transition hover:border-violet/40"
    >
      <div
        className="flex aspect-[1/1.414] w-full flex-col items-center justify-center gap-2 p-4 text-center"
        style={{
          backgroundColor: `${sectionColor ?? "var(--color-violet)"}0f`,
        }}
      >
        <span
          className="text-2xl font-semibold"
          style={{ color: sectionColor ?? "var(--color-violet)" }}
        >
          {questionCount != null ? `${questionCount} câu` : "Trắc nghiệm"}
        </span>
        {timeLimitMinutes ? (
          <span className="flex items-center gap-1 text-xs text-muted">
            <Clock3 className="size-3.5" aria-hidden /> {timeLimitMinutes} phút
          </span>
        ) : null}
      </div>
      <div className="flex flex-1 flex-col gap-2 p-3">
        <h3 className="line-clamp-2 text-sm font-medium leading-snug group-hover:text-violet">
          {item.title}
        </h3>
        <div className="mt-auto flex flex-wrap gap-1.5">
          {item.grade != null ? (
            <Badge variant="outline">Khối {item.grade}</Badge>
          ) : null}
          {item.subjectName ? (
            <Badge variant="outline">{item.subjectName}</Badge>
          ) : null}
          {untilAt ? (
            <Badge variant="warning" title={formatDateTime(untilAt)}>
              Đến {formatTime(untilAt)}
            </Badge>
          ) : null}
        </div>
      </div>
    </Link>
  );
}

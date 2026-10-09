import { useState } from "react";
import { useNavigate } from "react-router";
import {
  FileText,
  GraduationCap,
  Megaphone,
  PencilLine,
  Users,
} from "lucide-react";
import { useHome, useTaxonomy } from "@/features/home/api";
import {
  GradeChips,
  loadGrade,
  saveGrade,
} from "@/components/common/GradeChips";
import { SectionTile } from "@/components/common/SectionTile";
import { EmptyState } from "@/components/common/EmptyState";
import { QuizCard } from "@/components/common/ContentCard";
import { Skeleton } from "@/components/ui/skeleton";
import { Badge } from "@/components/ui/badge";

export function HomePage() {
  const navigate = useNavigate();
  const [grade, setGrade] = useState<number | null>(() => loadGrade());
  const { data: home } = useHome(grade);
  const { data: taxonomy } = useTaxonomy();

  const changeGrade = (g: number | null) => {
    setGrade(g);
    saveGrade(g);
  };

  return (
    <div className="o-li">
      <div className="mx-auto w-full max-w-6xl px-4 py-6">
        {/* Tìm kiếm lớn + chọn khối */}
        <section aria-label="Tìm kiếm" className="mb-8">
          <form
            className="mx-auto mb-4 max-w-2xl"
            role="search"
            onSubmit={(e) => {
              e.preventDefault();
              const q = new FormData(e.currentTarget)
                .get("q")
                ?.toString()
                .trim();
              navigate(
                q ? `/tim-kiem?q=${encodeURIComponent(q)}` : "/tim-kiem",
              );
            }}
          >
            <label htmlFor="hero-search" className="sr-only">
              Tìm tài liệu, bài tập…
            </label>
            <input
              id="hero-search"
              name="q"
              type="search"
              placeholder="Tìm tài liệu, bài tập… (gõ không dấu cũng được)"
              className="h-12 w-full rounded-card border border-grid bg-white px-4 text-base shadow-sm outline-none transition focus:border-violet focus:ring-4 focus:ring-violet/10"
            />
          </form>
          <div className="flex justify-center">
            <GradeChips value={grade} onChange={changeGrade} />
          </div>
        </section>

        {/* Bài tập đang mở */}
        <section aria-label="Bài tập đang mở" className="mb-10">
          <h2 className="mb-3 text-lg font-semibold">
            Bài tập đang mở{grade ? ` · Khối ${grade}` : ""}
          </h2>
          {home == null ? (
            <div className="flex gap-3 overflow-hidden">
              {[1, 2, 3].map((i) => (
                <Skeleton
                  key={i}
                  className="aspect-[1/1.414] w-44 shrink-0 rounded-card"
                />
              ))}
            </div>
          ) : home.openQuizzes.length === 0 ? (
            <EmptyState
              icon={PencilLine}
              title="Chưa có bài tập nào đang mở"
              description={
                grade
                  ? `Khối ${grade} chưa có bài tập mở. Thử khối khác hoặc quay lại sau.`
                  : "Giáo viên chưa mở bài tập nào. Quay lại sau nhé!"
              }
            />
          ) : (
            <div className="flex gap-3 overflow-x-auto pb-2 sm:grid sm:grid-cols-3 sm:overflow-visible lg:grid-cols-4">
              {home.openQuizzes.map((q) => (
                <QuizCard
                  key={q.id}
                  item={{
                    slug: q.slug,
                    id: q.id,
                    title: q.title,
                    grade: q.grade,
                    subjectName: q.subjectName,
                  }}
                  untilAt={q.untilAt}
                />
              ))}
            </div>
          )}
        </section>

        {/* Chuyên mục */}
        <section aria-label="Chuyên mục" className="mb-10">
          <h2 className="mb-3 text-lg font-semibold">Chuyên mục</h2>
          {taxonomy == null ? (
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
              {Array.from({ length: 7 }, (_, i) => (
                <Skeleton key={i} className="h-16" />
              ))}
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
              {taxonomy.sections
                .filter((s) => !s.isInternal)
                .map((s) => (
                  <SectionTile key={s.slug} section={s} />
                ))}
            </div>
          )}
        </section>

        {/* Thông báo + số liệu */}
        {(home?.announcements.length ?? 0) > 0 ? (
          <section aria-label="Thông báo" className="mb-10 space-y-2">
            {home!.announcements.map((a) => (
              <div
                key={a.id}
                className="flex items-start gap-2 rounded-card border border-grid bg-white p-3 text-sm"
              >
                <Megaphone
                  className="mt-0.5 size-4 shrink-0 text-violet"
                  aria-hidden
                />
                <div>
                  <span className="font-medium">{a.title}</span>
                  {a.isPinned ? <Badge className="ml-2">Ghim</Badge> : null}
                  <p
                    className="mt-1 whitespace-pre-line text-muted"
                    dangerouslySetInnerHTML={{ __html: a.bodyHtml }}
                  />
                </div>
              </div>
            ))}
          </section>
        ) : null}

        {home != null ? (
          <section
            aria-label="Số liệu"
            className="flex flex-wrap gap-6 border-t border-grid pt-6 text-sm text-muted"
          >
            <span className="flex items-center gap-1.5">
              <FileText className="size-4" aria-hidden />{" "}
              {home.stats.documents.toLocaleString("vi-VN")} tài liệu
            </span>
            <span className="flex items-center gap-1.5">
              <PencilLine className="size-4" aria-hidden />{" "}
              {home.stats.quizzes.toLocaleString("vi-VN")} bài tập
            </span>
            <span className="flex items-center gap-1.5">
              <Users className="size-4" aria-hidden />{" "}
              {home.stats.teachers.toLocaleString("vi-VN")} giáo viên
            </span>
            <span className="flex items-center gap-1.5">
              <GraduationCap className="size-4" aria-hidden /> Khối 1–5
            </span>
          </section>
        ) : null}
      </div>
    </div>
  );
}

import { useMemo, useState } from "react";
import { useParams } from "react-router";
import { useTaxonomy } from "@/features/home/api";
import { useCombinedItems, usePublicItems } from "@/features/documents/api";
import { ItemGrid } from "@/components/common/ItemGrid";
import {
  ItemFilters,
  DEFAULT_FILTERS,
  type ItemFilterState,
} from "@/components/common/ItemFilters";
import { Pagination } from "@/components/common/Pagination";
import { cn } from "@/lib/utils";

/** /khoi/:grade — lưới tài liệu + bài tập của khối (spec §5.1). */
export function KhoiPage() {
  const { grade: gradeParam } = useParams<{ grade: string }>();
  const grade = Number(gradeParam);
  const { data: taxonomy } = useTaxonomy();

  const [tab, setTab] = useState(""); // '' = Tất cả
  const [filters, setFilters] = useState<ItemFilterState>(DEFAULT_FILTERS);
  const [page, setPage] = useState(1);

  const sections = useMemo(
    () => (taxonomy?.sections ?? []).filter((s) => !s.isInternal),
    [taxonomy],
  );
  const section = sections.find((s) => s.slug === tab);

  // Tab "Tất cả" hoặc chuyên mục "Both" → gộp 2 kind; còn lại 1 kind theo nội dung chuyên mục.
  const isBoth = !section || section.contentKind === "Both";
  const baseParams = {
    grade: Number.isFinite(grade) ? grade : null,
    section: tab || null,
    subject: filters.subject || null,
    year: filters.year ? Number(filters.year) : null,
    week: filters.week ? Number(filters.week) : null,
    sort: filters.sort,
    page,
    pageSize: 24,
  };
  const combined = useCombinedItems(baseParams);
  const single = usePublicItems(
    {
      ...baseParams,
      kind: section?.contentKind === "Quiz" ? "quiz" : "document",
    },
    !isBoth,
  );

  const loading = isBoth ? combined.loading : single.isPending;
  const items = isBoth ? combined.items : (single.data?.items ?? []);
  const total = isBoth ? combined.total : (single.data?.total ?? 0);

  const resetPage = (next: ItemFilterState) => {
    setFilters(next);
    setPage(1);
  };
  const changeTab = (slug: string) => {
    setTab(slug);
    setPage(1);
  };

  return (
    <div className="o-li">
      <div className="mx-auto w-full max-w-6xl px-4 py-8">
        <h1 className="mb-4 text-xl font-semibold">
          Khối {Number.isFinite(grade) ? grade : ""}
        </h1>

        {/* Tab theo chuyên mục */}
        <div
          className="mb-4 flex flex-wrap gap-2"
          role="tablist"
          aria-label="Chuyên mục"
        >
          <button
            type="button"
            role="tab"
            aria-selected={tab === ""}
            onClick={() => changeTab("")}
            className={cn(
              "rounded-full border px-3 py-1.5 text-sm transition",
              tab === ""
                ? "border-violet bg-violet text-white"
                : "border-grid bg-white text-muted hover:border-violet/50 hover:text-violet",
            )}
          >
            Tất cả
          </button>
          {sections.map((s) => (
            <button
              key={s.slug}
              type="button"
              role="tab"
              aria-selected={tab === s.slug}
              onClick={() => changeTab(s.slug)}
              className={cn(
                "rounded-full border px-3 py-1.5 text-sm transition",
                tab === s.slug
                  ? "border-violet bg-violet text-white"
                  : "border-grid bg-white text-muted hover:border-violet/50 hover:text-violet",
              )}
            >
              {s.name}
            </button>
          ))}
        </div>

        <div className="mb-4">
          <ItemFilters value={filters} onChange={resetPage} />
        </div>

        <ItemGrid
          items={items}
          loading={loading}
          emptyTitle="Chưa có nội dung"
          emptyDescription={
            section
              ? `Chuyên mục ${section.name} của khối ${grade} chưa có nội dung nào đang hiển thị.`
              : `Khối ${grade} chưa có nội dung nào đang hiển thị.`
          }
        />
        <Pagination
          page={page}
          pageSize={24}
          total={total}
          onChange={setPage}
        />
      </div>
    </div>
  );
}

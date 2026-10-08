import { useMemo, useState } from "react";
import { Link, useParams } from "react-router";
import { useTaxonomy } from "@/features/home/api";
import { useCombinedItems, usePublicItems } from "@/features/documents/api";
import type { PublicItemRow } from "@/features/documents/types";
import {
  GradeChips,
  loadGrade,
  saveGrade,
} from "@/components/common/GradeChips";
import { ItemGrid } from "@/components/common/ItemGrid";
import {
  ItemFilters,
  DEFAULT_FILTERS,
  type ItemFilterState,
} from "@/components/common/ItemFilters";
import { Pagination } from "@/components/common/Pagination";
import { EmptyState } from "@/components/common/EmptyState";
import { FileQuestion } from "lucide-react";

/** Nhóm bài tập cuối tuần theo Tuần, tuần lớn trước (spec §5.1). */
function WeekGroups({ items }: { items: PublicItemRow[] }) {
  const groups = useMemo(() => {
    const m = new Map<number | null, PublicItemRow[]>();
    for (const it of items) {
      const key = it.weekNo;
      const arr = m.get(key) ?? [];
      arr.push(it);
      m.set(key, arr);
    }
    return [...m.entries()].sort((a, b) => (b[0] ?? 0) - (a[0] ?? 0));
  }, [items]);

  return (
    <div className="space-y-8">
      {groups.map(([week, list]) => (
        <section
          key={week ?? "none"}
          aria-label={week ? `Tuần ${week}` : "Không rõ tuần"}
        >
          <h2 className="mb-3 text-base font-semibold text-violet">
            {week ? `Tuần ${week}` : "Tuần khác"}
          </h2>
          <ItemGrid items={list} />
        </section>
      ))}
    </div>
  );
}

/** /chuyen-muc/:section — danh sách theo chuyên mục, lọc khối (spec §5.1). */
export function ChuyenMucPage() {
  const { section: sectionSlug } = useParams<{ section: string }>();
  const { data: taxonomy } = useTaxonomy();
  const section = taxonomy?.sections.find((s) => s.slug === sectionSlug);

  const [grade, setGrade] = useState<number | null>(() => loadGrade());
  const [filters, setFilters] = useState<ItemFilterState>(DEFAULT_FILTERS);
  const [page, setPage] = useState(1);

  const isBtct = section?.slug === "bai-tap-cuoi-tuan";
  const grouped = isBtct && !filters.week;
  const isBoth = section ? section.contentKind === "Both" : false;
  const baseParams = {
    grade,
    section: sectionSlug,
    subject: filters.subject || null,
    year: filters.year ? Number(filters.year) : null,
    week: filters.week ? Number(filters.week) : null,
    sort: filters.sort,
  };

  // BTCT (không lọc tuần) hoặc chuyên mục "Both" → gộp 2 kind; còn lại 1 kind.
  const combinedEnabled = grouped || isBoth;
  const combined = useCombinedItems(
    { ...baseParams, page: grouped ? 1 : page, pageSize: grouped ? 100 : 24 },
    combinedEnabled,
  );
  const single = usePublicItems(
    {
      ...baseParams,
      kind: section?.contentKind === "Quiz" ? "quiz" : "document",
      page,
      pageSize: 24,
    },
    !combinedEnabled,
  );

  if (taxonomy != null && section == null) {
    return (
      <div className="mx-auto w-full max-w-6xl px-4 py-8">
        <EmptyState
          icon={FileQuestion}
          title="Không tìm thấy chuyên mục"
          description="Chuyên mục không tồn tại hoặc đã bị vô hiệu."
          action={
            <Link to="/" className="text-sm text-violet hover:underline">
              Về trang chủ
            </Link>
          }
        />
      </div>
    );
  }

  const items = combinedEnabled ? combined.items : (single.data?.items ?? []);
  const total = grouped
    ? combined.items.length
    : combinedEnabled
      ? combined.total
      : (single.data?.total ?? 0);
  const loading = combinedEnabled ? combined.loading : single.isPending;

  return (
    <div className="o-li">
      <div className="mx-auto w-full max-w-6xl px-4 py-8">
        <h1 className="mb-4 text-xl font-semibold">
          {section?.name ?? "Chuyên mục"}
        </h1>

        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <GradeChips
            value={grade}
            onChange={(g) => {
              setGrade(g);
              saveGrade(g);
              setPage(1);
            }}
          />
          <ItemFilters
            value={filters}
            onChange={(next) => {
              setFilters(next);
              setPage(1);
            }}
            showWeek={!grouped}
          />
        </div>

        {grouped ? (
          combined.loading ? (
            <div className="space-y-4">
              {[1, 2].map((i) => (
                <div key={i} className="grid grid-cols-2 gap-3 md:grid-cols-4">
                  {Array.from({ length: 4 }, (_, j) => (
                    <div
                      key={j}
                      className="aspect-[3/4] rounded-card bg-grid/40"
                    />
                  ))}
                </div>
              ))}
            </div>
          ) : items.length === 0 ? (
            <EmptyState
              title="Chưa có bài tập cuối tuần"
              description="Bài tập đang hiển thị sẽ được nhóm theo tuần ở đây."
            />
          ) : (
            <WeekGroups items={items} />
          )
        ) : (
          <>
            <ItemGrid
              items={items}
              loading={loading}
              emptyTitle="Chưa có nội dung"
              emptyDescription={
                section
                  ? `Chuyên mục ${section.name} chưa có nội dung nào đang hiển thị.`
                  : "Chưa có nội dung nào đang hiển thị."
              }
            />
            <Pagination
              page={page}
              pageSize={24}
              total={total}
              onChange={setPage}
            />
          </>
        )}
      </div>
    </div>
  );
}

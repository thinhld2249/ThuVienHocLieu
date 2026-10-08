import { useEffect, useState } from "react";
import { useSearchParams } from "react-router";
import { Search } from "lucide-react";
import { useCombinedItems } from "@/features/documents/api";
import { ItemGrid } from "@/components/common/ItemGrid";
import {
  ItemFilters,
  DEFAULT_FILTERS,
  type ItemFilterState,
} from "@/components/common/ItemFilters";
import { Pagination } from "@/components/common/Pagination";
import {
  GradeChips,
  loadGrade,
  saveGrade,
} from "@/components/common/GradeChips";

/** Tìm kiếm gộp tài liệu + bài tập (spec §5.1) — hỗ trợ gõ không dấu. */
export function TimKiemPage() {
  const [params, setParams] = useSearchParams();
  const q = params.get("q")?.trim() ?? "";

  const [grade, setGrade] = useState<number | null>(() => loadGrade());
  const [filters, setFilters] = useState<ItemFilterState>(DEFAULT_FILTERS);
  const [page, setPage] = useState(1);

  // q rỗng → không gọi API (tránh list toàn bộ nội dung).
  const active = q.length > 0;
  const { items, total, loading } = useCombinedItems(
    {
      q: active ? q : null,
      grade,
      subject: filters.subject || null,
      year: filters.year ? Number(filters.year) : null,
      week: filters.week ? Number(filters.week) : null,
      sort: filters.sort,
      page,
      pageSize: 24,
    },
    active,
  );

  useEffect(() => {
    setPage(1);
  }, [q, grade, filters]);

  const submit = (value: string) => {
    const v = value.trim();
    setParams(v ? { q: v } : {});
  };

  return (
    <div className="o-li">
      <div className="mx-auto w-full max-w-6xl px-4 py-8">
        <form
          role="search"
          className="mb-4"
          onSubmit={(e) => {
            e.preventDefault();
            const v = new FormData(e.currentTarget).get("q")?.toString() ?? "";
            submit(v);
          }}
        >
          <label htmlFor="search-q" className="sr-only">
            Tìm tài liệu, bài tập
          </label>
          <div className="relative max-w-2xl">
            <Search
              className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted"
              aria-hidden
            />
            <input
              id="search-q"
              name="q"
              type="search"
              defaultValue={q}
              placeholder="Tìm tài liệu, bài tập… (gõ không dấu cũng được)"
              className="h-11 w-full rounded-card border border-grid bg-white pl-9 pr-4 text-base outline-none transition focus:border-violet focus:ring-4 focus:ring-violet/10"
            />
          </div>
        </form>

        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <GradeChips
            value={grade}
            onChange={(g) => {
              setGrade(g);
              saveGrade(g);
            }}
          />
          <ItemFilters value={filters} onChange={setFilters} />
        </div>

        {!active ? (
          <div className="flex flex-col items-center gap-2 rounded-card border border-dashed border-grid bg-white/60 px-6 py-12 text-center">
            <Search className="size-8 text-muted/60" aria-hidden />
            <h2 className="font-medium">Nhập từ khóa để tìm</h2>
            <p className="max-w-md text-sm text-muted">
              Kết quả gộp tài liệu và bài tập, hỗ trợ gõ không dấu — ví dụ “ke
              hoach chu nhiem” khớp “Kế hoạch chủ nhiệm”.
            </p>
          </div>
        ) : (
          <>
            <p className="mb-3 text-sm text-muted" aria-live="polite">
              {loading ? "Đang tìm…" : `${total} kết quả cho “${q}”`}
            </p>
            <ItemGrid
              items={items}
              loading={loading}
              emptyTitle="Không tìm thấy kết quả"
              emptyDescription="Thử từ khóa khác, ít dấu hơn, hoặc bỏ bớt bộ lọc."
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

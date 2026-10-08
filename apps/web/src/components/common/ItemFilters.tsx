import { useTaxonomy } from '@/features/home/api'

/**
 * Bộ lọc chung cho trang browse/tìm kiếm (spec §5.1): môn, tuần, năm học,
 * sắp xếp. Giá trị rỗng = "Tất cả" (không gửi param).
 */
export interface ItemFilterState {
  subject: string
  week: string
  year: string
  sort: 'new' | 'popular'
}

export const DEFAULT_FILTERS: ItemFilterState = {
  subject: '',
  week: '',
  year: '',
  sort: 'new',
}

export function ItemFilters({
  value,
  onChange,
  showWeek = true,
}: {
  value: ItemFilterState
  onChange: (next: ItemFilterState) => void
  showWeek?: boolean
}) {
  const { data } = useTaxonomy()
  const subjects = data?.subjects ?? []
  const years = data?.schoolYears ?? []

  const set = (patch: Partial<ItemFilterState>) => {
    onChange({ ...value, ...patch })
  }

  const cls =
    'h-9 rounded-btn border border-grid bg-white px-2 text-sm text-ink outline-none focus:border-violet'

  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Bộ lọc">
      <select
        aria-label="Lọc theo môn học"
        className={cls}
        value={value.subject}
        onChange={(e) => set({ subject: e.target.value })}
      >
        <option value="">Môn: Tất cả</option>
        {subjects.map((s) => (
          <option key={s.id} value={s.slug}>
            {s.name}
          </option>
        ))}
      </select>
      {showWeek ? (
        <select
          aria-label="Lọc theo tuần"
          className={cls}
          value={value.week}
          onChange={(e) => set({ week: e.target.value })}
        >
          <option value="">Tuần: Tất cả</option>
          {Array.from({ length: 35 }, (_, i) => i + 1).map((w) => (
            <option key={w} value={w}>
              Tuần {w}
            </option>
          ))}
        </select>
      ) : null}
      <select
        aria-label="Lọc theo năm học"
        className={cls}
        value={value.year}
        onChange={(e) => set({ year: e.target.value })}
      >
        <option value="">Năm học: Tất cả</option>
        {years.map((y) => (
          <option key={y.id} value={y.id}>
            {y.name}
          </option>
        ))}
      </select>
      <select
        aria-label="Sắp xếp"
        className={cls}
        value={value.sort}
        onChange={(e) => set({ sort: e.target.value as ItemFilterState['sort'] })}
      >
        <option value="new">Mới nhất</option>
        <option value="popular">Xem nhiều</option>
      </select>
    </div>
  )
}

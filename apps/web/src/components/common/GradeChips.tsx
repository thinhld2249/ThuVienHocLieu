import { cn } from '@/lib/utils'

const GRADE_KEY = 'hl_grade'

export function loadGrade(): number | null {
  const raw = localStorage.getItem(GRADE_KEY)
  const n = raw == null ? NaN : Number(raw)
  return Number.isInteger(n) && n >= 1 && n <= 5 ? n : null
}

export function saveGrade(grade: number | null) {
  if (grade == null) localStorage.removeItem(GRADE_KEY)
  else localStorage.setItem(GRADE_KEY, String(grade))
}

/** Chip Khối 1–5 + Tất cả; nhớ lựa chọn trong localStorage (spec §5.1). */
export function GradeChips({
  value,
  onChange,
  className,
}: {
  value: number | null
  onChange: (grade: number | null) => void
  className?: string
}) {
  return (
    <div className={cn('flex flex-wrap items-center gap-2', className)} role="group" aria-label="Chọn khối lớp">
      <span className="text-sm font-medium text-muted">Khối:</span>
      <button
        type="button"
        onClick={() => onChange(null)}
        aria-pressed={value == null}
        className={cn(
          'h-9 rounded-full border px-4 text-sm font-medium transition',
          value == null
            ? 'border-violet bg-violet text-white'
            : 'border-grid bg-white text-muted hover:border-violet/50 hover:text-violet',
        )}
      >
        Tất cả
      </button>
      {[1, 2, 3, 4, 5].map((g) => (
        <button
          key={g}
          type="button"
          onClick={() => onChange(g)}
          aria-pressed={value === g}
          className={cn(
            'size-9 rounded-full border text-sm font-medium transition',
            value === g
              ? 'border-violet bg-violet text-white'
              : 'border-grid bg-white text-muted hover:border-violet/50 hover:text-violet',
          )}
        >
          {g}
        </button>
      ))}
    </div>
  )
}

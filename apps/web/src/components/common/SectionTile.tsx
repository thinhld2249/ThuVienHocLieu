import { Link } from 'react-router'
import {
  BookOpen,
  CalendarCheck,
  CalendarRange,
  FileSearch,
  Lightbulb,
  PencilLine,
  Presentation,
  Users,
  type LucideIcon,
} from 'lucide-react'
import type { SectionDto } from '@/features/home/api'
import { cn } from '@/lib/utils'

const icons: Record<string, LucideIcon> = {
  presentation: Presentation,
  'calendar-range': CalendarRange,
  'book-open': BookOpen,
  'pencil-line': PencilLine,
  'file-search': FileSearch,
  lightbulb: Lightbulb,
  'calendar-check': CalendarCheck,
  users: Users,
}

/** Ô chuyên mục: icon nền màu 12% + dải màu cạnh trái (spec §14.2/§14.3). */
export function SectionTile({ section, count }: { section: SectionDto; count?: number }) {
  const Icon = icons[section.icon ?? ''] ?? BookOpen
  const color = section.color ?? 'var(--color-violet)'

  return (
    <Link
      to={`/chuyen-muc/${section.slug}`}
      className="group relative flex items-center gap-3 overflow-hidden rounded-card border border-grid bg-white p-3 transition hover:border-violet/40"
    >
      <span aria-hidden className="absolute inset-y-0 left-0 w-1" style={{ backgroundColor: color }} />
      <span
        className="flex size-10 shrink-0 items-center justify-center rounded-btn"
        style={{ backgroundColor: `${color}1f`, color }}
      >
        <Icon className="size-5" aria-hidden />
      </span>
      <span className="min-w-0">
        <span className="block truncate text-sm font-medium group-hover:text-violet">{section.name}</span>
        {typeof count === 'number' ? (
          <span className={cn('block text-xs text-muted')}>{count} tài liệu</span>
        ) : null}
      </span>
    </Link>
  )
}

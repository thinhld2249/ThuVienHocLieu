import { Link } from 'react-router'
import { BookOpen, HeartOff, ListChecks } from 'lucide-react'
import { useFavorites, useRemoveFavorite } from '@/features/documents/api'
import { EmptyState } from '@/components/common/EmptyState'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDateTime } from '@/lib/date'
import { contentUrl } from '@/lib/slug'

/** /gv/yeu-thich — tài liệu/bài tập đã lưu (spec §5.2). */
export function YeuThichPage() {
  const { data, isPending } = useFavorites()
  const remove = useRemoveFavorite()

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-xl font-semibold">Yêu thích</h1>
        <Link to="/gv/tai-lieu">
          <Button variant="ghost" size="sm">
            <ListChecks className="size-4" aria-hidden /> Tài liệu của tôi
          </Button>
        </Link>
      </div>

      {isPending ? (
        <div className="space-y-2">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-14" />
          ))}
        </div>
      ) : !data || data.length === 0 ? (
        <EmptyState
          title="Chưa có mục yêu thích nào"
          description="Mở trang chi tiết một tài liệu hoặc bài tập và bấm “Lưu vào yêu thích” để tìm lại nhanh sau này."
        />
      ) : (
        <ul className="space-y-2">
          {data.map((f) => (
            <li
              key={`${f.kind}-${f.itemId}`}
              className="flex items-center gap-3 rounded-card border border-grid bg-white p-3"
            >
              {f.kind === 'quiz' ? (
                <ListChecks className="size-5 shrink-0 text-dks" aria-hidden />
              ) : (
                <BookOpen className="size-5 shrink-0 text-sec-bai-giang" aria-hidden />
              )}
              <div className="min-w-0 flex-1">
                <Link
                  to={contentUrl(f.kind === 'quiz' ? 'bai-tap' : 'tai-lieu', f.slug, f.itemId)}
                  className="truncate text-sm font-medium hover:text-violet"
                >
                  {f.title}
                </Link>
                <p className="mt-0.5 text-xs text-muted">
                  {f.sectionName ?? 'Chuyên mục khác'}
                  {f.grade != null ? ` · Khối ${f.grade}` : ''} · Đã lưu {formatDateTime(f.addedAt)}
                </p>
              </div>
              <Badge className="hidden shrink-0 sm:inline-flex">
                {f.kind === 'quiz' ? 'Bài tập' : 'Tài liệu'}
              </Badge>
              <Button
                size="icon"
                variant="ghost"
                className="size-8 shrink-0"
                aria-label={`Bỏ yêu thích ${f.title}`}
                disabled={remove.isPending}
                onClick={() => void remove.mutateAsync({ itemType: f.kind, itemId: f.itemId })}
              >
                <HeartOff className="size-4" aria-hidden />
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

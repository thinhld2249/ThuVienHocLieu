import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api-client'
import { EmptyState } from '@/components/common/EmptyState'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'

interface StaticPage {
  slug: string
  title: string
  bodyMarkdown: string
  updatedAt: string | null
}

export function TrangPage() {
  const { slug } = useParams<{ slug: string }>()
  const { data, isLoading, error } = useQuery({
    queryKey: ['public', 'page', slug],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/public/pages/{slug}', {
        params: { path: { slug: slug! } },
      })
      if (error || !data) throw new Error('Không tải được trang')
      return data as unknown as StaticPage
    },
    enabled: !!slug,
  })

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-8">
      {isLoading ? <Skeleton className="h-64" /> : null}
      {error || !data ? (
        <EmptyState
          title="Không tìm thấy trang"
          action={
            <Link to="/">
              <Button variant="outline">Về trang chủ</Button>
            </Link>
          }
        />
      ) : (
        <article>
          <h1 className="mb-4 text-2xl font-semibold">{data.title}</h1>
          {data.bodyMarkdown ? (
            <div className="prose prose-sm max-w-none">{data.bodyMarkdown}</div>
          ) : (
            <p className="text-muted">Trang đang được cập nhật.</p>
          )}
        </article>
      )}
    </div>
  )
}

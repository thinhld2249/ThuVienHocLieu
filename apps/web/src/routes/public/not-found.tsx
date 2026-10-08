import { Link } from 'react-router'
import { SearchX } from 'lucide-react'
import { Button } from '@/components/ui/button'

export function NotFoundPage() {
  return (
    <div className="o-li flex min-h-dvh flex-col items-center justify-center gap-3 px-4 text-center">
      <SearchX className="size-12 text-muted/50" aria-hidden />
      <h1 className="text-2xl font-semibold">Không tìm thấy trang</h1>
      <p className="max-w-md text-muted">
        Trang bạn tìm không tồn tại hoặc đã được ẩn. Hãy quay về trang chủ và thử lại.
      </p>
      <Link to="/">
        <Button variant="outline">Về trang chủ</Button>
      </Link>
    </div>
  )
}

import { Link } from 'react-router'
import { Lock } from 'lucide-react'
import { EmptyState } from '@/components/common/EmptyState'
import { Button } from '@/components/ui/button'

export function GvPlaceholder() {
  return (
    <div className="mx-auto max-w-2xl pt-16">
      <EmptyState
        icon={Lock}
        title="Khu giáo viên"
        description="ĐĂNG NHẬP BẰNG GOOGLE ĐỂ TIẾP TỤC — khu giáo viên (tài liệu, bài tập, lớp, tổ) được mở theo từng giai đoạn."
        action={
          <Link to="/">
            <Button variant="outline">Về trang chủ</Button>
          </Link>
        }
      />
    </div>
  )
}

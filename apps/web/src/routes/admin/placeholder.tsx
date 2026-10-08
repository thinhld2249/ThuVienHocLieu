import { Link } from 'react-router'
import { ShieldCheck } from 'lucide-react'
import { EmptyState } from '@/components/common/EmptyState'
import { Button } from '@/components/ui/button'

export function AdminPlaceholder() {
  return (
    <div className="mx-auto max-w-2xl pt-16">
      <EmptyState
        icon={ShieldCheck}
        title="Khu quản trị"
        description="Chỉ dành cho người quản trị hệ thống. Chức năng quản lý sẽ được mở theo từng giai đoạn."
        action={
          <Link to="/">
            <Button variant="outline">Về trang chủ</Button>
          </Link>
        }
      />
    </div>
  )
}

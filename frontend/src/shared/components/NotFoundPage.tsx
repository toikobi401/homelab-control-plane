import { Link } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { PageContainer } from './PageContainer'

export function NotFoundPage() {
  return (
    <PageContainer className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Không có trang này</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Đường dẫn không khớp với màn hình nào của hub.
        </p>
      </div>
      <Button asChild variant="outline" className="w-full">
        <Link to="/">Về trang trạng thái</Link>
      </Button>
    </PageContainer>
  )
}

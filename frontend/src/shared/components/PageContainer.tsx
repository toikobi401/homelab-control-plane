import type { ReactNode } from 'react'

import { cn } from '@/shared/lib/utils'

/**
 * Bề rộng đọc được cho trang thường.
 *
 * `<main>` không còn bó bề rộng nữa (trang nhúng MeshCentral cần dùng hết chỗ),
 * nên trang thường tự bó ở đây. Một chỗ duy nhất định nghĩa "rộng bao nhiêu là
 * vừa" — sửa số này là sửa cho cả ứng dụng, không phải đi từng file.
 *
 * Điện thoại căn giữa cho cân; desktop căn trái để bám sidebar.
 */
export function PageContainer({
  children,
  className,
}: {
  children: ReactNode
  className?: string
}) {
  return (
    <div className={cn('mx-auto w-full max-w-3xl lg:mx-0 lg:max-w-5xl', className)}>{children}</div>
  )
}

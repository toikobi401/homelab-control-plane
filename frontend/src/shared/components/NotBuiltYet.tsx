import { Construction } from 'lucide-react'

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

interface NotBuiltYetProps {
  title: string
  /** Năng lực này sẽ làm gì, theo CONTEXT.md §1. */
  description: string
  /** Điều kiện còn thiếu trước khi làm được — nói thẳng, đừng để trống. */
  blockedBy: string
}

/**
 * Chỗ giữ chỗ cho một năng lực chưa xây.
 *
 * CONTEXT.md §9 cấm "màn hình mock được trình bày như đã chạy được". Trang này là
 * cách ngược lại: nó nói rõ chưa có gì, và đang chờ cái gì. Không dữ liệu giả,
 * không nút bấm không làm gì.
 */
export function NotBuiltYet({ title, description, blockedBy }: NotBuiltYetProps) {
  return (
    // Giới hạn bề rộng trên desktop: thẻ kéo dài hết màn hình cho vài dòng chữ
    // làm mắt phải quét ngang quá xa. Điện thoại vốn đã hẹp nên không cần.
    <Card className="lg:max-w-2xl">
      <CardHeader>
        <div className="flex items-center gap-2">
          <Construction className="size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
          <CardTitle>{title}</CardTitle>
        </div>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent className="text-sm text-muted-foreground">
        <p>
          <span className="font-medium text-foreground">Chưa xây.</span> {blockedBy}
        </p>
      </CardContent>
    </Card>
  )
}

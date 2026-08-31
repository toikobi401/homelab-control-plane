import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { buttonVariants } from '@/components/ui/button'
import { cn } from '@/shared/lib/utils'
import type { PowerAction } from '@/shared/api/deviceControl'

import { POWER_ACTION_LABELS } from './powerActions'

interface PowerActionDialogProps {
  action: PowerAction | null
  hostname: string
  isPending: boolean
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Hỏi lại trước khi gửi lệnh điều khiển nguồn.
 *
 * Đây là màn hình duy nhất trong hệ thống gây hậu quả vật lý **không lấy lại
 * được**: máy tắt rồi thì chỉ đánh thức được nếu phần cứng hỗ trợ, và mọi việc
 * đang dở trên máy đó mất theo. Một cú chạm nhầm trên điện thoại không được
 * phép làm điều đó — nên có hộp thoại này, và nút xác nhận không phải nút mặc
 * định được focus sẵn.
 */
export function PowerActionDialog({
  action,
  hostname,
  isPending,
  onConfirm,
  onCancel,
}: PowerActionDialogProps) {
  const label = action ? POWER_ACTION_LABELS[action] : null

  return (
    <AlertDialog open={action !== null} onOpenChange={(open) => !open && onCancel()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {label?.verb} {hostname}?
          </AlertDialogTitle>
          <AlertDialogDescription>{label?.consequence}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Huỷ</AlertDialogCancel>
          <AlertDialogAction
            onClick={onConfirm}
            disabled={isPending}
            className={cn(
              action !== 'lock' && buttonVariants({ variant: 'destructive' }),
              'min-w-28',
            )}
          >
            {isPending ? 'Đang gửi…' : label?.verb}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

import { useHealth } from '@/shared/api/health'
import { cn } from '@/shared/lib/utils'

/**
 * Chấm trạng thái backend trên thanh tiêu đề.
 *
 * Ba trạng thái tách bạch (§7): đang tải, lỗi, và sống. "Lỗi" ở đây thường nghĩa
 * là điện thoại rớt khỏi tailnet — thông tin quan trọng với người dùng, nên nó
 * hiện thường trực chứ không nằm trong màn hình riêng.
 */
export function HealthIndicator() {
  const { data, isPending, isError } = useHealth()

  const state = isPending ? 'pending' : isError ? 'error' : 'ok'

  const label = {
    pending: 'Đang kiểm tra kết nối tới hub',
    error: 'Mất kết nối tới hub',
    ok: 'Hub đang hoạt động',
  }[state]

  const text = { pending: 'Đang kết nối', error: 'Mất kết nối', ok: 'Trực tuyến' }[state]

  return (
    <span
      className="flex items-center gap-2 text-xs text-muted-foreground"
      // role=status để trình đọc màn hình thông báo khi mất kết nối,
      // thay vì người dùng phải tự phát hiện chấm đổi màu.
      role="status"
      aria-label={label}
    >
      <span
        className={cn(
          'size-2 rounded-full',
          state === 'ok' && 'bg-success',
          state === 'error' && 'bg-destructive',
          state === 'pending' && 'animate-pulse bg-muted-foreground',
        )}
        aria-hidden="true"
      />
      <span>{text}</span>
      {data ? <span className="sr-only">{data.status}</span> : null}
    </span>
  )
}

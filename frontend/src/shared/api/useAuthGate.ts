import { ApiError } from './client'
import { useAuthStatus, useSessions } from './auth'

/** Màn hình nào được phép hiện, quyết định từ trạng thái backend. */
export type AuthGate =
  | { state: 'loading' }
  /** Backend không trả lời — chưa biết gì để quyết định. */
  | { state: 'unreachable'; error: Error }
  /** Chưa đặt mật khẩu: phải đặt lần đầu, và chỉ làm được từ localhost (§6.3). */
  | { state: 'needs-setup' }
  | { state: 'needs-login' }
  | { state: 'authenticated' }

/**
 * Gộp `/api/auth/status` và `/api/auth/sessions` thành một trạng thái duy nhất.
 *
 * Vì sao cần hai lời gọi: `status` chỉ nói hệ thống đã có mật khẩu chưa, nó
 * **không** nói người đang xem đã đăng nhập chưa. Phiên nằm trong cookie HttpOnly
 * mà JavaScript không đọc được (§6.3), nên cách duy nhất để biết là gọi một
 * endpoint cần xác thực và xem có 401 không.
 */
export function useAuthGate(): AuthGate {
  const status = useAuthStatus()

  // Chưa đặt mật khẩu thì chắc chắn chưa có phiên — đừng gọi thừa một 401.
  const sessionsEnabled = status.data?.passwordConfigured === true
  const sessions = useSessions({ enabled: sessionsEnabled })

  if (status.isPending) {
    return { state: 'loading' }
  }

  if (status.isError) {
    return { state: 'unreachable', error: status.error }
  }

  if (!status.data.passwordConfigured) {
    return { state: 'needs-setup' }
  }

  if (sessions.isPending) {
    return { state: 'loading' }
  }

  if (sessions.isError) {
    // 401 là câu trả lời hợp lệ "chưa đăng nhập", không phải hỏng hóc. Mọi lỗi
    // khác mới là backend có vấn đề — phân biệt hai thứ này, nếu không người
    // dùng thấy màn hình đăng nhập mỗi khi mạng chập chờn.
    if (sessions.error instanceof ApiError && sessions.error.isUnauthorized) {
      return { state: 'needs-login' }
    }
    return { state: 'unreachable', error: sessions.error }
  }

  return { state: 'authenticated' }
}

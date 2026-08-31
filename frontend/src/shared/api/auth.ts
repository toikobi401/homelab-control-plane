import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'

import { apiFetch, type components } from './client'

/** Kiểu sinh từ OpenAPI (§3). Không viết tay. */
export type AuthStatus = components['schemas']['AuthStatus']
export type SessionDto = components['schemas']['SessionDto']
export type RevokeAllResponse = components['schemas']['RevokeAllResponse']

export const authStatusQueryKey = ['auth', 'status'] as const
export const sessionsQueryKey = ['auth', 'sessions'] as const

/**
 * Dọn cache khi phiên vừa kết thúc (đăng xuất, hoặc thu hồi cả phiên hiện tại).
 *
 * `removeQueries` chứ không phải `clear`: dữ liệu của phiên cũ phải biến mất
 * khỏi bộ nhớ, nhưng các query đang mount vẫn phải fetch lại để `AuthGate` biết
 * là giờ đã 401 và đưa người dùng về màn hình đăng nhập. `clear()` xoá cả
 * observer nên component đang hiện không refetch — kẹt ở màn hình lỗi giữa
 * ứng dụng. Đã gặp thật khi kiểm chứng "đăng xuất tất cả thiết bị".
 */
function resetAfterSessionEnded(queryClient: QueryClient): void {
  queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== 'auth' })
  void queryClient.invalidateQueries({ queryKey: ['auth'] })
}

/**
 * Hệ thống đã đặt mật khẩu chưa.
 *
 * Quyết định màn hình đầu tiên người dùng thấy: chưa đặt → màn hình đặt mật khẩu
 * lần đầu (chỉ chạy được từ localhost, §6.3); đã đặt → màn hình đăng nhập.
 */
export function useAuthStatus() {
  return useQuery({
    queryKey: authStatusQueryKey,
    queryFn: ({ signal }) => apiFetch<AuthStatus>('/api/auth/status', { signal }),
    // Trạng thái này gần như không đổi sau lần đặt đầu tiên.
    staleTime: 5 * 60_000,
  })
}

/**
 * Phiên đang mở của chính người dùng.
 *
 * 401 nghĩa là chưa đăng nhập — đó là câu trả lời hợp lệ, không phải lỗi hệ
 * thống. `useSession` bên dưới dựa vào đây để biết đã đăng nhập hay chưa.
 */
export function useSessions(options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: sessionsQueryKey,
    queryFn: ({ signal }) => apiFetch<SessionDto[]>('/api/auth/sessions', { signal }),
    enabled: options.enabled ?? true,
  })
}

export function useLogin() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (password: string) =>
      apiFetch<SessionDto>('/api/auth/login', { method: 'POST', body: { password } }),
    onSuccess: () => {
      // Đăng nhập xong thì mọi truy vấn phụ thuộc phiên đều cũ.
      void queryClient.invalidateQueries()
    },
  })
}

export function useSetupPassword() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (password: string) =>
      apiFetch<void>('/api/auth/setup', { method: 'POST', body: { password } }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: authStatusQueryKey })
    },
  })
}

export function useLogout() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => apiFetch<void>('/api/auth/logout', { method: 'POST' }),
    onSuccess: () => resetAfterSessionEnded(queryClient),
  })
}

/** Thu hồi một phiên cụ thể — dùng khi mất thiết bị (§6.3). */
export function useRevokeSession() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (sessionId: string) =>
      apiFetch<void>(`/api/auth/sessions/${sessionId}`, { method: 'DELETE' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
  })
}

/**
 * Đăng xuất tất cả thiết bị.
 *
 * `keepCurrent` quyết định phiên hiện tại có sống sót không — backend xoá cookie
 * khi `keepCurrent=false`, nên khi đó phải dọn sạch cache y như logout.
 */
export function useRevokeAllSessions() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (keepCurrent: boolean) =>
      apiFetch<RevokeAllResponse>(`/api/auth/sessions/revoke-all?keepCurrent=${keepCurrent}`, {
        method: 'POST',
      }),
    onSuccess: (_data, keepCurrent) => {
      if (keepCurrent) {
        void queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
      } else {
        resetAfterSessionEnded(queryClient)
      }
    },
  })
}

export function useChangePassword() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: { currentPassword: string; newPassword: string }) =>
      apiFetch<void>('/api/auth/password', { method: 'POST', body: input }),
    onSuccess: () => {
      // Đổi mật khẩu huỷ mọi phiên khác (§6.3) — danh sách phiên đã cũ.
      void queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
  })
}

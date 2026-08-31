import { useQuery } from '@tanstack/react-query'

import { apiFetch, type HealthResponse } from './client'

export const healthQueryKey = ['health'] as const

/**
 * Trạng thái sống của backend.
 *
 * Đây là endpoint duy nhất frontend gọi được ở Phase 0, và nó cũng là cách người
 * dùng kiểm tra hub còn sống khi mở từ điện thoại qua tailnet (CONTEXT.md §10).
 */
export function useHealth() {
  return useQuery({
    queryKey: healthQueryKey,
    queryFn: ({ signal }) => apiFetch<HealthResponse>('/health', { signal }),
    refetchInterval: 15_000,
  })
}

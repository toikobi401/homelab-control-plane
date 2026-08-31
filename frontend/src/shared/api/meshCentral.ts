import { useQuery } from '@tanstack/react-query'

import { apiFetch } from './client'

/**
 * Cấu hình MeshCentral — công cụ quản lý thiết bị mà hub nhúng vào (§2.3).
 *
 * Địa chỉ lấy từ backend chứ không hardcode: nó khác nhau giữa máy dev và máy
 * thật, và iframe chạy trên máy NGƯỜI DÙNG nên phải là địa chỉ họ gọi tới được
 * (tailnet), không phải localhost của máy chạy hub.
 */
export interface MeshCentralConfig {
  configured: boolean
  url: string | null
}

export const meshCentralConfigQueryKey = ['meshcentral', 'config'] as const

export function useMeshCentralConfig() {
  return useQuery({
    queryKey: meshCentralConfigQueryKey,
    queryFn: ({ signal }) => apiFetch<MeshCentralConfig>('/api/meshcentral/config', { signal }),

    // Cấu hình chỉ đổi khi người vận hành sửa và khởi động lại backend —
    // không cần hỏi lại liên tục.
    staleTime: 5 * 60_000,
  })
}

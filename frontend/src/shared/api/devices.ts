import { useQuery } from '@tanstack/react-query'

import { apiFetch, type components } from './client'

/** Kiểu sinh từ OpenAPI (§3). Không viết tay. */
export type DeviceDto = components['schemas']['DeviceDto']
export type DeviceListDto = components['schemas']['DeviceListDto']

export const devicesQueryKey = ['devices'] as const

/**
 * Thiết bị trong tailnet kèm trạng thái hiện diện (năng lực 1).
 *
 * Backend đã cache 30 giây để không gọi Tailscale mỗi lần mở trang, nên làm mới
 * ở đây rẻ. 30 giây khớp với cache đó: hỏi dày hơn chỉ nhận lại cùng một câu
 * trả lời, còn thưa hơn thì dữ liệu cũ hơn mức cần thiết.
 */
export function useDevices() {
  return useQuery({
    queryKey: devicesQueryKey,
    queryFn: ({ signal }) => apiFetch<DeviceListDto>('/api/devices', { signal }),
    refetchInterval: 30_000,
  })
}

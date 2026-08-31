import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { apiFetch, type components } from './client'

/** Kiểu sinh từ OpenAPI (§3). Không viết tay. */
export type RegisteredDeviceDto = components['schemas']['RegisteredDeviceDto']
export type CommandAuditDto = components['schemas']['CommandAuditDto']

/**
 * Tập hành động điều khiển nguồn. Khớp enum `PowerAction` ở backend, và **đóng**
 * đúng như §5a quy định — mỗi hành động là một endpoint riêng, không có chỗ nào
 * nhận chuỗi lệnh tuỳ ý.
 */
export const POWER_ACTIONS = ['shutdown', 'restart', 'sleep', 'lock'] as const
export type PowerAction = (typeof POWER_ACTIONS)[number]

export const registeredDevicesQueryKey = ['devices', 'registered'] as const
export const deviceCommandsQueryKey = ['devices', 'commands'] as const

/** Thiết bị đã đăng ký kèm trạng thái duyệt (§5a). */
export function useRegisteredDevices() {
  return useQuery({
    queryKey: registeredDevicesQueryKey,
    queryFn: ({ signal }) => apiFetch<RegisteredDeviceDto[]>('/api/devices/registered', { signal }),
    refetchInterval: 30_000,
  })
}

/** Nhật ký kiểm toán lệnh điều khiển (§5a điều 7). */
export function useDeviceCommands() {
  return useQuery({
    queryKey: deviceCommandsQueryKey,
    queryFn: ({ signal }) => apiFetch<CommandAuditDto[]>('/api/devices/commands', { signal }),
  })
}

/**
 * Gửi một lệnh điều khiển nguồn.
 *
 * Không có `retry`: mặc định của mutation đã là không thử lại, và điều đó quan
 * trọng ở đây hơn mọi chỗ khác — gửi lại "restart" vì tưởng lần đầu hỏng có thể
 * khởi động lại máy hai lần, cắt ngang việc người dùng đang làm.
 */
export function usePowerAction() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ deviceId, action }: { deviceId: string; action: PowerAction }) =>
      apiFetch<void>(`/api/devices/${deviceId}/${action}`, { method: 'POST' }),
    onSettled: () => {
      // Thành công hay thất bại đều vào nhật ký kiểm toán — làm mới cả hai lần.
      void queryClient.invalidateQueries({ queryKey: deviceCommandsQueryKey })
      void queryClient.invalidateQueries({ queryKey: ['devices'] })
    },
  })
}

export function useApproveDevice() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (deviceId: string) =>
      apiFetch<void>(`/api/devices/${deviceId}/approve`, { method: 'POST' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: registeredDevicesQueryKey })
    },
  })
}

export function useRevokeDeviceApproval() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (deviceId: string) =>
      apiFetch<void>(`/api/devices/${deviceId}/revoke-approval`, { method: 'POST' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: registeredDevicesQueryKey })
    },
  })
}

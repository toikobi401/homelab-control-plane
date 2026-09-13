import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { apiFetch, type components } from './client'

/** Kiểu sinh từ OpenAPI (§3). Không viết tay. */
export type BackupStatusDto = components['schemas']['BackupStatusDto']
export type BackupJobDto = components['schemas']['BackupJobDto']
export type BackupRunDto = components['schemas']['BackupRunDto']
export type DirectoryListingDto = components['schemas']['DirectoryListingDto']
export type DirectoryEntryDto = components['schemas']['DirectoryEntryDto']
export type FilterPresetDto = components['schemas']['FilterPresetDto']
export type FilterContentDto = components['schemas']['FilterContentDto']
export type SaveJobRequest = components['schemas']['SaveJobRequest']
export type SaveJobResultDto = components['schemas']['SaveJobResultDto']

/** Giá trị `BackupRunStatus` bên .NET, gửi xuống dưới dạng chuỗi. */
export type BackupRunStatus = 'Running' | 'Succeeded' | 'Failed' | 'Cancelled'

export const backupStatusQueryKey = ['backup', 'status'] as const
export const backupHistoryQueryKey = ['backup', 'history'] as const

/**
 * OpenAPI khai int32/int64 là `number | string` vì JSON không có kiểu số
 * nguyên 64-bit — số lớn có thể tới dưới dạng chuỗi. Ép một lần ở đây để phần
 * còn lại của giao diện chỉ phải làm việc với `number`.
 */
export function toNumber(value: number | string): number {
  return typeof value === 'number' ? value : Number(value)
}

/**
 * Danh sách công việc sao lưu kèm lần chạy gần nhất (năng lực 3).
 *
 * Làm mới 10 giây thay vì 30 như trang thiết bị: một lần sao lưu chạy trong
 * vài giây tới vài phút, nên `isRunning` phải đổi kịp cho người dùng thấy.
 * Backend không cache endpoint này nhưng nó chỉ đọc DB nội bộ, gọi dày không
 * tốn gì đáng kể.
 */
export function useBackupStatus() {
  return useQuery({
    queryKey: backupStatusQueryKey,
    queryFn: ({ signal }) => apiFetch<BackupStatusDto>('/api/backup', { signal }),
    refetchInterval: 10_000,
  })
}

/** Lịch sử các lần chạy, mới nhất trước. */
export function useBackupHistory(limit = 20) {
  return useQuery({
    queryKey: [...backupHistoryQueryKey, limit] as const,
    queryFn: ({ signal }) =>
      apiFetch<BackupRunDto[]>(`/api/backup/history?limit=${limit}`, { signal }),
    refetchInterval: 10_000,
  })
}

/**
 * Chạy một công việc ngay.
 *
 * Backend trả 409 khi job đang chạy — không xếp hàng, từ chối luôn. Nơi gọi
 * hiển thị thông báo đó chứ không thử lại: bấm hai lần không nên thành hai lần
 * sao lưu.
 *
 * Endpoint này cần antiforgery token; `apiFetch` tự đính kèm cho POST.
 */
export function useRunBackupJob() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (jobName: string) =>
      apiFetch<BackupRunDto>(`/api/backup/${encodeURIComponent(jobName)}/run`, { method: 'POST' }),
    onSettled: () => {
      // Cả hai đều đổi sau một lần chạy: trạng thái job và lịch sử. Làm mới kể
      // cả khi lỗi — job có thể đã bắt đầu rồi mới hỏng giữa chừng.
      void queryClient.invalidateQueries({ queryKey: backupStatusQueryKey })
      void queryClient.invalidateQueries({ queryKey: backupHistoryQueryKey })
    },
  })
}

export const backupBrowseQueryKey = ['backup', 'browse'] as const
export const backupPresetsQueryKey = ['backup', 'presets'] as const

/**
 * Duyệt thư mục trên máy chạy hub để chọn nguồn sao lưu.
 *
 * `path` rỗng thì backend trả danh sách thư mục gốc được phép (`BrowseRoots`),
 * không phải toàn bộ ổ đĩa — xem docs/backup-setup.md.
 *
 * Không `refetchInterval`: cây thư mục không tự đổi trong lúc người dùng đang
 * chọn, hỏi lại liên tục chỉ tốn công.
 */
export function useBrowseDirectories(path: string | null, enabled = true) {
  return useQuery({
    queryKey: [...backupBrowseQueryKey, path] as const,
    queryFn: ({ signal }) => {
      const query = path ? `?path=${encodeURIComponent(path)}` : ''
      return apiFetch<DirectoryListingDto>(`/api/backup/browse${query}`, { signal })
    },
    enabled,
  })
}

/** Mẫu nội dung file lọc. Tĩnh, nên giữ lâu trong cache. */
export function useFilterPresets(enabled = true) {
  return useQuery({
    queryKey: backupPresetsQueryKey,
    queryFn: ({ signal }) => apiFetch<FilterPresetDto[]>('/api/backup/presets', { signal }),
    staleTime: Infinity,
    enabled,
  })
}

/** Nội dung file lọc hiện tại của một công việc. */
export function useJobFilter(jobName: string | null) {
  return useQuery({
    queryKey: [...backupStatusQueryKey, 'filter', jobName] as const,
    queryFn: ({ signal }) =>
      apiFetch<FilterContentDto>(
        `/api/backup/jobs/${encodeURIComponent(jobName!)}/filter`,
        { signal },
      ),
    enabled: Boolean(jobName),
  })
}

/**
 * Tạo hoặc sửa một công việc.
 *
 * Backend trả 409 khi tên trùng với công việc khai tay trong file cấu hình —
 * công việc khai tay luôn thắng, nên lưu đè sẽ tạo ra thứ không bao giờ chạy.
 */
export function useSaveBackupJob() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: SaveJobRequest) =>
      apiFetch<SaveJobResultDto>('/api/backup/jobs', {
        method: 'POST',
        // apiFetch tự JSON.stringify và tự đặt Content-Type — stringify ở đây
        // nữa sẽ gửi một chuỗi JSON lồng trong JSON, backend trả 400 rỗng.
        body: request,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: backupStatusQueryKey })
    },
  })
}

/**
 * Xoá một công việc do người dùng tạo.
 *
 * Không xoá file `.backupignore` trong thư mục nguồn — nó nằm trong thư mục của
 * người dùng, xoá file ở đó vượt quá thứ họ vừa yêu cầu.
 */
export function useDeleteBackupJob() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (jobName: string) =>
      apiFetch<void>(`/api/backup/jobs/${encodeURIComponent(jobName)}`, { method: 'DELETE' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: backupStatusQueryKey })
    },
  })
}

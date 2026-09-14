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
export type RemoteDto = components['schemas']['RemoteDto']
export type UploadResultDto = components['schemas']['UploadResultDto']

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
 * Duyệt thư mục **trên máy chạy hub** để chọn nguồn sao lưu.
 *
 * `path` rỗng thì backend trả danh sách mọi ổ đĩa cố định; phạm vi giới hạn bằng
 * danh sách CHẶN (`Backup:BlockedPaths`), không phải danh sách cho phép — đổi từ
 * 2026-09-13, xem docs/backup-setup.md.
 *
 * Chỉ dùng cho chế độ "thư mục có sẵn trên máy chạy hub". Muốn sao lưu thư mục
 * của một máy khác thì dùng chế độ tải lên — xem `useUploadFolder`.
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

/** Một tệp đã chọn, kèm đường dẫn tương đối trong thư mục gốc. */
export interface UploadEntry {
  file: File
  /** Ví dụ `Anh/2026/img.jpg` — lấy từ `webkitRelativePath`, đã bỏ tên thư mục gốc. */
  relativePath: string
}

/** Tiến trình tải lên, tính theo BYTE chứ không theo số tệp. */
export interface UploadProgress {
  sentBytes: number
  totalBytes: number
  received: number
  duplicates: number
  rejected: number
}

/**
 * Cỡ một lô.
 *
 * Gửi cả thư mục trong một request là sai: mất kết nối ở phút thứ 20 là mất
 * sạch, và không hiện được tiến trình. Chia theo CẢ HAI ngưỡng vì chúng bắt hai
 * kiểu thư mục khác nhau — 3000 ảnh thumbnail chạm ngưỡng số tệp, còn 5 video 4K
 * chạm ngưỡng dung lượng.
 *
 * Đừng hạ số tệp xuống quá thấp: rate limit là 300 request/phút, lô 50 tệp cho
 * 60 request khi tải 3000 tệp — còn xa trần. Lô 5 tệp thì chạm.
 */
const BATCH_MAX_FILES = 50
const BATCH_MAX_BYTES = 50 * 1024 * 1024

/** Chia danh sách tệp thành các lô vừa giới hạn của backend. */
export function planBatches(entries: UploadEntry[]): UploadEntry[][] {
  const batches: UploadEntry[][] = []
  let current: UploadEntry[] = []
  let currentBytes = 0

  for (const entry of entries) {
    // Tệp đơn lớn hơn ngưỡng vẫn phải đi một mình, không bỏ qua.
    if (current.length > 0 && (current.length >= BATCH_MAX_FILES || currentBytes + entry.file.size > BATCH_MAX_BYTES)) {
      batches.push(current)
      current = []
      currentBytes = 0
    }

    current.push(entry)
    currentBytes += entry.file.size
  }

  if (current.length > 0) {
    batches.push(current)
  }

  return batches
}

/**
 * Tải một thư mục từ MÁY ĐANG MỞ WEB lên hub (§5d).
 *
 * Máy chủ không đọc được đĩa của máy khác, kể cả trong tailnet — giới hạn hệ
 * điều hành. Nên muốn sao lưu thư mục của một máy khác thì mở web UI tại chính
 * máy đó; trình duyệt đọc thư mục rồi đẩy nội dung lên đây. Sau đó thư mục này
 * thành `Source` của một job rclone bình thường.
 *
 * Gửi TUẦN TỰ từng lô, không song song: song song làm tiến trình nhảy loạn và dễ
 * chạm rate limit.
 *
 * Đường dẫn tương đối phải gửi riêng trong `paths` vì `Content-Disposition` chỉ
 * mang tên trần (`img.jpg`) — cấu trúc thư mục con chỉ có trong
 * `webkitRelativePath` bên JS.
 */
export function useUploadFolder() {
  return useMutation({
    mutationFn: async ({
      folder,
      entries,
      onProgress,
      signal,
    }: {
      folder: string
      entries: UploadEntry[]
      onProgress?: (progress: UploadProgress) => void
      signal?: AbortSignal
    }): Promise<UploadProgress> => {
      const totalBytes = entries.reduce((sum, entry) => sum + entry.file.size, 0)
      const progress: UploadProgress = {
        sentBytes: 0,
        totalBytes,
        received: 0,
        duplicates: 0,
        rejected: 0,
      }

      for (const batch of planBatches(entries)) {
        const form = new FormData()
        form.append('folder', folder)

        // paths và files phải cùng thứ tự: backend ghép cặp theo thứ tự đọc.
        for (const entry of batch) {
          form.append('paths', entry.relativePath)
        }
        for (const entry of batch) {
          form.append('files', entry.file, entry.file.name)
        }

        const result = await apiFetch<UploadResultDto>('/api/backup/upload', {
          method: 'POST',
          body: form,
          signal,
        })

        progress.received += toNumber(result.received)
        progress.duplicates += toNumber(result.duplicates)
        progress.rejected += toNumber(result.rejected)
        progress.sentBytes += batch.reduce((sum, entry) => sum + entry.file.size, 0)

        onProgress?.({ ...progress })
      }

      return progress
    },
  })
}

export const backupRemotesQueryKey = ['backup', 'remotes'] as const

/**
 * Remote đã khai trong rclone.conf.
 *
 * Để người dùng chọn thay vì gõ tay: tên remote phân biệt hoa thường, gõ `Hub:`
 * trong khi remote tên `hub` thì job hỏng lúc CHẠY chứ không phải lúc lưu.
 *
 * Danh sách rỗng nghĩa là không đọc được (rclone cũ, hoặc chưa cấu hình) —
 * giao diện rơi về ô gõ tự do thay vì chặn người dùng.
 */
export function useRcloneRemotes(enabled = true) {
  return useQuery({
    queryKey: backupRemotesQueryKey,
    queryFn: ({ signal }) => apiFetch<RemoteDto[]>('/api/backup/remotes', { signal }),
    staleTime: 60_000,
    enabled,
  })
}

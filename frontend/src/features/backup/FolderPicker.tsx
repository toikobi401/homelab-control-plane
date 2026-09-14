import { useState } from 'react'
import { ChevronRight, CornerLeftUp, Folder, HardDrive, Loader2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useBrowseDirectories } from '@/shared/api/backup'

/**
 * Chọn thư mục trên máy chạy hub.
 *
 * Duyệt được mọi ổ đĩa cố định; backend chỉ chặn vài thư mục nhạy cảm
 * (`Backup:BlockedPaths`) và trả lỗi khi chạm vào chúng.
 *
 * Đi kèm ô nhập đường dẫn ở `JobDialog`, không thay thế nó: biết đường dẫn rồi
 * thì dán vào nhanh hơn, còn cây thư mục dùng khi chưa nhớ rõ.
 *
 * Vì sao không dùng `showDirectoryPicker` **ở đây**: nó chọn thư mục trên MÁY
 * ĐANG MỞ TRÌNH DUYỆT, không phải máy chạy hub — và vì bảo mật nó không trả
 * đường dẫn tuyệt đối, thứ rclone bắt buộc phải có cho chế độ này.
 *
 * ⚠️ Lý do đó CHỈ đúng cho chế độ "thư mục có sẵn trên máy chạy hub". Muốn sao
 * lưu thư mục của máy đang mở web thì có chế độ tải lên riêng: trình duyệt đọc
 * nội dung thư mục rồi đẩy lên hub, nên không cần đường dẫn tuyệt đối. Đừng đọc
 * đoạn trên rồi kết luận cả tính năng tải lên là bất khả thi.
 */
export function FolderPicker({
  value,
  onChange,
}: {
  value: string | null
  onChange: (path: string) => void
}) {
  // Thư mục đang MỞ để xem, khác với thư mục đã CHỌN. Mở một thư mục không có
  // nghĩa là chọn nó — người dùng thường đi sâu vài cấp rồi mới quyết định.
  const [openPath, setOpenPath] = useState<string | null>(value)

  const listing = useBrowseDirectories(openPath)

  if (listing.isPending) {
    return (
      <div className="space-y-2 rounded-md border p-3" aria-busy="true">
        <Skeleton className="h-4 w-40" />
        <Skeleton className="h-4 w-32" />
        <Skeleton className="h-4 w-36" />
      </div>
    )
  }

  if (listing.isError) {
    return (
      <div className="rounded-md border border-destructive/50 p-3 text-sm text-destructive">
        {listing.error.message}
      </div>
    )
  }

  const data = listing.data
  const atRoot = data.path === null

  return (
    <div className="rounded-md border">
      {/* Thanh đường dẫn: luôn thấy đang đứng ở đâu, kể cả khi đi sâu nhiều cấp. */}
      <div className="flex items-center gap-2 border-b px-3 py-2 text-xs">
        {data.parent !== null || !atRoot ? (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-6 gap-1 px-1.5 text-xs"
            onClick={() => setOpenPath(data.parent)}
          >
            <CornerLeftUp className="size-3" aria-hidden="true" />
            Lên trên
          </Button>
        ) : null}

        <span className="truncate text-muted-foreground" title={data.path ?? undefined}>
          {data.path ?? 'Chọn ổ đĩa hoặc thư mục'}
        </span>
      </div>

      <ul className="max-h-56 overflow-y-auto py-1">
        {data.entries.length === 0 ? (
          <li className="px-3 py-4 text-center text-xs text-muted-foreground">
            Không có thư mục con.
          </li>
        ) : null}

        {data.entries.map((entry) => {
          const selected = value === entry.path

          return (
            <li key={entry.path}>
              <div
                className={
                  'flex items-center gap-1 px-1.5 ' +
                  (selected ? 'bg-accent/60' : 'hover:bg-accent/40')
                }
              >
                {/* Hai hành động tách riêng: bấm tên để CHỌN, bấm mũi tên để MỞ.
                    Gộp làm một thì không chọn được thư mục có thư mục con. */}
                <button
                  type="button"
                  className="flex min-w-0 flex-1 items-center gap-2 py-1.5 text-left text-sm"
                  onClick={() => onChange(entry.path)}
                >
                  {atRoot ? (
                    <HardDrive className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                  ) : (
                    <Folder className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                  )}
                  <span className="truncate">{entry.name}</span>
                </button>

                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  className="shrink-0"
                  onClick={() => setOpenPath(entry.path)}
                  aria-label={`Mở ${entry.name}`}
                >
                  <ChevronRight className="size-3.5" aria-hidden="true" />
                </Button>
              </div>
            </li>
          )
        })}
      </ul>

      {listing.isFetching ? (
        <div className="flex items-center gap-1.5 border-t px-3 py-1.5 text-xs text-muted-foreground">
          <Loader2 className="size-3 animate-spin" aria-hidden="true" />
          Đang đọc…
        </div>
      ) : null}
    </div>
  )
}

import { useState } from 'react'
import { Loader2, Lock, LockOpen } from 'lucide-react'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { useFilterPresets, useSaveBackupJob } from '@/shared/api/backup'

import { FolderPicker } from './FolderPicker'

/**
 * Tạo một công việc sao lưu: chọn thư mục, khai đích, soạn bộ lọc.
 *
 * Công việc lưu vào `backup-jobs.json` riêng, không ghi vào file cấu hình chính
 * của hub — xem docs/backup-setup.md để biết vì sao.
 */
export function JobDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [name, setName] = useState('')
  const [source, setSource] = useState<string | null>(null)
  const [destination, setDestination] = useState('')
  const [encrypted, setEncrypted] = useState(false)
  const [deleteExtra, setDeleteExtra] = useState(false)
  const [filterContent, setFilterContent] = useState('')

  const presets = useFilterPresets(open)
  const saveJob = useSaveBackupJob()

  function reset() {
    setName('')
    setSource(null)
    setDestination('')
    setEncrypted(false)
    setDeleteExtra(false)
    setFilterContent('')
    saveJob.reset()
  }

  function handleOpenChange(next: boolean) {
    if (!next) { reset() }
    onOpenChange(next)
  }

  function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (!source) { return }

    saveJob.mutate(
      {
        name: name.trim(),
        source,
        destination: destination.trim(),
        encrypted,
        deleteExtra,
        filterContent: filterContent.trim() === '' ? null : filterContent,
      },
      { onSuccess: () => handleOpenChange(false) },
    )
  }

  const canSubmit = name.trim() !== '' && source !== null && destination.trim() !== ''

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>Công việc sao lưu mới</DialogTitle>
          <DialogDescription>
            Chọn thư mục trên máy chạy hub và đích trên cloud.
          </DialogDescription>
        </DialogHeader>

        {/* Thân cuộn được: cây thư mục và ô soạn bộ lọc dài hơn màn hình điện thoại. */}
        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col gap-4">
          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto">
            <div className="space-y-1.5">
              <Label htmlFor="job-name">Tên</Label>
              <Input
                id="job-name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="tai-lieu"
                autoComplete="off"
              />
              <p className="text-xs text-muted-foreground">
                Chỉ chữ, số, dấu gạch ngang và gạch dưới.
              </p>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="job-source">Thư mục nguồn</Label>

              {/* Ô nhập đứng trước cây thư mục: biết đường dẫn rồi thì dán vào
                  là xong, nhanh hơn bấm qua nhiều cấp. Ctrl+L rồi Ctrl+C trong
                  File Explorer là có sẵn đường dẫn đầy đủ. */}
              <Input
                id="job-source"
                value={source ?? ''}
                onChange={(event) => setSource(event.target.value || null)}
                placeholder="D:\Du lieu\anh"
                autoComplete="off"
                spellCheck={false}
                className="font-mono text-xs"
              />
              <p className="text-xs text-muted-foreground">
                Gõ hoặc dán đường dẫn, hoặc chọn trong cây bên dưới.
              </p>

              <FolderPicker value={source} onChange={setSource} />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="job-destination">Đích trên cloud</Label>
              <Input
                id="job-destination"
                value={destination}
                onChange={(event) => setDestination(event.target.value)}
                placeholder="hub:backup/tai-lieu"
                autoComplete="off"
              />
              <p className="text-xs text-muted-foreground">
                Dạng <code>remote:đường/dẫn</code> của rclone. Trỏ vào remote đã mã hoá thì bật ổ
                khoá bên dưới.
              </p>
            </div>

            <div className="space-y-2">
              {/* Hai cờ quyết định mức an toàn — để chúng cạnh nhau, nói thẳng
                  hệ quả thay vì giấu sau thuật ngữ sync/copy. */}
              <label className="flex items-start gap-2.5 text-sm">
                <input
                  type="checkbox"
                  checked={encrypted}
                  onChange={(event) => setEncrypted(event.target.checked)}
                  className="mt-0.5 size-4"
                />
                <span className="flex-1">
                  <span className="flex items-center gap-1.5 font-medium">
                    {encrypted ? (
                      <Lock className="size-3.5" aria-hidden="true" />
                    ) : (
                      <LockOpen className="size-3.5" aria-hidden="true" />
                    )}
                    Đích đã mã hoá
                  </span>
                  <span className="text-xs text-muted-foreground">
                    Chỉ là nhãn hiển thị. Mã hoá thật do remote <code>crypt</code> của rclone làm.
                  </span>
                </span>
              </label>

              <label className="flex items-start gap-2.5 text-sm">
                <input
                  type="checkbox"
                  checked={deleteExtra}
                  onChange={(event) => setDeleteExtra(event.target.checked)}
                  className="mt-0.5 size-4"
                />
                <span className="flex-1">
                  <span className="font-medium">Xoá tệp thừa ở đích</span>
                  <span className="text-xs text-muted-foreground">
                    {deleteExtra
                      ? 'Xoá nhầm ở máy sẽ lan lên cloud. Bản sao lưu mất giá trị cứu hộ.'
                      : 'Chỉ thêm, không xoá — an toàn hơn.'}
                  </span>
                </span>
              </label>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="job-filter">Bỏ qua tệp (tuỳ chọn)</Label>

              {presets.data && presets.data.length > 0 ? (
                <div className="flex flex-wrap gap-1.5">
                  {presets.data.map((preset) => (
                    <Button
                      key={preset.name}
                      type="button"
                      variant="outline"
                      size="xs"
                      title={preset.description}
                      onClick={() => setFilterContent(preset.content)}
                    >
                      {preset.name}
                    </Button>
                  ))}
                </div>
              ) : null}

              <Textarea
                id="job-filter"
                value={filterContent}
                onChange={(event) => setFilterContent(event.target.value)}
                placeholder={'- *.tmp\n- node_modules/**\n+ **'}
                className="min-h-28 font-mono text-xs"
                spellCheck={false}
              />
              <p className="text-xs text-muted-foreground">
                Cú pháp rclone: <code>-</code> bỏ qua, <code>+</code> giữ lại, <code>**</code> khớp
                nhiều cấp. Dòng <code>+ **</code> ở cuối là bắt buộc, thiếu nó thì tệp không khớp
                luật nào sẽ bị bỏ qua.
              </p>
              <p className="text-xs text-muted-foreground">
                Lưu thành <code>.backupignore</code> trong chính thư mục nguồn.
              </p>
            </div>
          </div>

          {saveJob.isError ? (
            <Alert variant="destructive">
              <AlertDescription>{saveJob.error.message}</AlertDescription>
            </Alert>
          ) : null}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => handleOpenChange(false)}>
              Huỷ
            </Button>
            <Button type="submit" disabled={!canSubmit || saveJob.isPending}>
              {saveJob.isPending ? (
                <>
                  <Loader2 className="size-4 animate-spin" aria-hidden="true" />
                  Đang lưu
                </>
              ) : (
                'Tạo công việc'
              )}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

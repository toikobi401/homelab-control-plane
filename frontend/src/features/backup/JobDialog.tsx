import { useRef, useState } from 'react'
import { FolderUp, HardDrive, Loader2, Lock, LockOpen } from 'lucide-react'

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
import {
  useFilterPresets,
  useRcloneRemotes,
  useSaveBackupJob,
  useUploadFolder,
  type UploadEntry,
  type UploadProgress,
} from '@/shared/api/backup'
import { formatBytes } from '@/shared/lib/time'

import { FolderPicker } from './FolderPicker'

/**
 * Thư mục mẹ trên cloud cho mọi công việc sao lưu.
 *
 * Gom lại một chỗ thay vì rải ra gốc Drive lẫn với thư mục cá nhân. Dữ liệu đã
 * mã hoá nằm ở `HubBackup/encrypted` — đó là cấu hình của remote crypt trong
 * rclone.conf, không phải thứ giao diện đặt.
 */
const DEFAULT_DESTINATION_PREFIX = 'HubBackup/'

/**
 * Hai cách chỉ ra thư mục nguồn.
 *
 * `upload` — chọn thư mục trên MÁY ĐANG MỞ WEB, trình duyệt đọc nội dung rồi đẩy
 * lên hub. Đây là cách duy nhất sao lưu được thư mục của một máy khác: máy chủ
 * không đọc được đĩa của máy khác, kể cả trong tailnet (§5d).
 *
 * `server` — thư mục có sẵn trên chính máy chạy hub. Vẫn giữ vì tải 200 GB qua
 * HTTP lên đúng cái máy đang chứa nó là vô nghĩa.
 */
type SourceMode = 'upload' | 'server'

/** Tên thư mục gốc + danh sách tệp bên trong, lấy từ hộp thoại chọn thư mục. */
interface PickedFolder {
  name: string
  entries: UploadEntry[]
  totalBytes: number
}

/**
 * Đọc thứ `<input webkitdirectory>` trả về.
 *
 * `webkitRelativePath` có dạng `TênThưMục/con/tệp.jpg` — đoạn đầu là tên thư mục
 * gốc, phải cắt bỏ vì nó đã thành tên thư mục trên hub rồi; giữ lại sẽ lồng
 * thêm một cấp thừa.
 */
function readPickedFolder(files: FileList): PickedFolder | null {
  if (files.length === 0) { return null }

  const entries: UploadEntry[] = []
  let rootName = ''
  let totalBytes = 0

  for (const file of files) {
    const relative = file.webkitRelativePath || file.name
    const segments = relative.split('/')

    if (segments.length > 1) {
      rootName ||= segments[0] ?? ''
      entries.push({ file, relativePath: segments.slice(1).join('/') })
    } else {
      // Safari trên iOS không hỗ trợ chọn thư mục, rơi về chọn nhiều tệp —
      // lúc đó không có cấu trúc thư mục nào để giữ.
      entries.push({ file, relativePath: relative })
    }

    totalBytes += file.size
  }

  return { name: rootName || 'tai-len', entries, totalBytes }
}

/** Bỏ dấu tiếng Việt và ký tự lạ — tên thư mục cũng là tên job gợi ý. */
function toSlug(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .replace(/[^a-zA-Z0-9-_]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .toLowerCase()
}

/**
 * Tạo một công việc sao lưu: chọn thư mục, khai đích, soạn bộ lọc.
 *
 * Công việc lưu vào `backup-jobs.json` riêng, không ghi vào file cấu hình chính
 * của hub — xem docs/backup-setup.md để biết vì sao.
 */
export function JobDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [mode, setMode] = useState<SourceMode>('server')
  const [name, setName] = useState('')
  const [source, setSource] = useState<string | null>(null)
  const [picked, setPicked] = useState<PickedFolder | null>(null)
  const [progress, setProgress] = useState<UploadProgress | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  // Tách đích thành hai phần: remote chọn từ danh sách, đường dẫn gõ tay.
  // Gộp làm một ô khiến gõ nhầm hoa thường ("Hub:" thay vì "hub:") và job hỏng
  // lúc CHẠY, không phải lúc lưu — người dùng chỉ thấy "rclone thất bại (mã 1)".
  const [remote, setRemote] = useState('')
  // Mọi job mặc định nằm dưới một thư mục mẹ trên Drive, thay vì rải ra gốc
  // lẫn với thư mục cá nhân. Người dùng vẫn sửa được nếu muốn chỗ khác.
  const [destinationPath, setDestinationPath] = useState(DEFAULT_DESTINATION_PREFIX)
  const [encrypted, setEncrypted] = useState(false)
  const [deleteExtra, setDeleteExtra] = useState(false)
  const [filterContent, setFilterContent] = useState('')

  const presets = useFilterPresets(open)
  const remotes = useRcloneRemotes(open)
  const uploadFolder = useUploadFolder()

  // Danh sách rỗng nghĩa là không đọc được rclone.conf — rơi về ô gõ tự do thay
  // vì chặn người dùng vì lỗi của ta.
  const remoteList = remotes.data ?? []
  const canPickRemote = remoteList.length > 0

  const destination = canPickRemote
    ? (remote === '' ? '' : `${remote}:${destinationPath.replace(/^\/+/, '')}`)
    : destinationPath
  const saveJob = useSaveBackupJob()

  function reset() {
    setMode('server')
    setName('')
    setSource(null)
    setPicked(null)
    setProgress(null)
    setRemote('')
    setDestinationPath(DEFAULT_DESTINATION_PREFIX)
    setEncrypted(false)
    setDeleteExtra(false)
    setFilterContent('')
    saveJob.reset()
    uploadFolder.reset()
  }

  function handleOpenChange(next: boolean) {
    if (!next) { reset() }
    onOpenChange(next)
  }

  function handlePick(event: React.ChangeEvent<HTMLInputElement>) {
    const result = event.target.files ? readPickedFolder(event.target.files) : null
    setPicked(result)

    // Gợi ý tên job từ tên thư mục — sửa được, chỉ là đỡ phải gõ.
    if (result && name.trim() === '') {
      setName(toSlug(result.name))
    }
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()

    const jobName = name.trim()

    if (mode === 'upload') {
      if (!picked) { return }

      // Tải hết các lô TRƯỚC rồi mới tạo job: tạo job trước mà tải lỗi giữa
      // chừng thì còn lại một job trỏ vào thư mục thiếu tệp — một bản sao lưu
      // sai lệch mà không có gì báo.
      const folderSlug = toSlug(picked.name) || jobName

      try {
        await uploadFolder.mutateAsync({
          folder: folderSlug,
          entries: picked.entries,
          onProgress: setProgress,
        })
      } catch {
        return // Lỗi đã nằm trong uploadFolder.error, hiện ở dưới.
      }

      saveJob.mutate(
        {
          name: jobName,
          source: '',
          uploadFolder: folderSlug,
          destination: destination.trim(),
          encrypted,
          deleteExtra,
          filterContent: filterContent.trim() === '' ? null : filterContent,
        },
        { onSuccess: () => handleOpenChange(false) },
      )
      return
    }

    if (!source) { return }

    saveJob.mutate(
      {
        name: jobName,
        source,
        destination: destination.trim(),
        encrypted,
        deleteExtra,
        filterContent: filterContent.trim() === '' ? null : filterContent,
      },
      { onSuccess: () => handleOpenChange(false) },
    )
  }

  const hasSource = mode === 'upload' ? picked !== null : source !== null
  const canSubmit = name.trim() !== '' && hasSource && destination.trim() !== ''
  const busy = saveJob.isPending || uploadFolder.isPending

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>Công việc sao lưu mới</DialogTitle>
          <DialogDescription>
            Chọn thư mục cần sao lưu và đích trên cloud.
          </DialogDescription>
        </DialogHeader>

        {/* Thân cuộn được: cây thư mục và ô soạn bộ lọc dài hơn màn hình điện thoại. */}
        {/* void: handleSubmit là async, mà onSubmit mong đợi hàm trả về void.
            Lỗi bên trong đã bắt sẵn và hiện qua uploadFolder.error/saveJob.error. */}
        <form
          onSubmit={(event) => void handleSubmit(event)}
          className="flex min-h-0 flex-1 flex-col gap-4"
        >
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

            {/* Hai chế độ nguồn. Máy chủ không đọc được đĩa máy khác, nên muốn
                sao lưu thư mục của máy đang mở web thì phải tải nội dung lên. */}
            <div className="space-y-1.5">
              <span className="text-sm font-medium">Thư mục nguồn</span>

              <div className="grid gap-2 sm:grid-cols-2">
                <ModeButton
                  active={mode === 'upload'}
                  onClick={() => setMode('upload')}
                  icon={<FolderUp className="size-4" aria-hidden="true" />}
                  title="Tải lên từ máy này"
                  hint="Chọn thư mục trên máy đang mở web"
                />
                <ModeButton
                  active={mode === 'server'}
                  onClick={() => setMode('server')}
                  icon={<HardDrive className="size-4" aria-hidden="true" />}
                  title="Có sẵn trên máy chạy hub"
                  hint="Không tốn băng thông"
                />
              </div>
            </div>

            {mode === 'upload' ? (
              <div className="space-y-2">
                <input
                  ref={fileInput}
                  type="file"
                  webkitdirectory=""
                  directory=""
                  multiple
                  className="hidden"
                  aria-label="Chọn thư mục để tải lên"
                  onChange={handlePick}
                />

                <Button type="button" variant="outline" onClick={() => fileInput.current?.click()}>
                  <FolderUp className="size-4" aria-hidden="true" />
                  {picked ? 'Chọn thư mục khác' : 'Chọn thư mục'}
                </Button>

                {picked ? (
                  <p className="text-sm">
                    <span className="font-medium">{picked.name}</span>
                    <span className="text-muted-foreground">
                      {' · '}
                      {picked.entries.length} tệp · {formatBytes(picked.totalBytes)}
                    </span>
                  </p>
                ) : (
                  <p className="text-xs text-muted-foreground">
                    Chọn cả thư mục; các thư mục con bên trong giữ nguyên cấu trúc.
                  </p>
                )}

                {progress ? <UploadBar progress={progress} /> : null}

                <p className="text-xs text-muted-foreground">
                  Tệp tải lên nằm lại trên máy chạy hub, nên lần sau chỉ tải phần mới. Đóng cửa sổ
                  giữa chừng thì phần đã tải vẫn giữ — chọn lại đúng thư mục đó để tiếp tục.
                </p>
              </div>
            ) : (
              <div className="space-y-1.5">
                <Label htmlFor="job-source">Đường dẫn trên máy chạy hub</Label>

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
            )}

            <div className="space-y-1.5">
              <Label htmlFor="job-destination">Đích trên cloud</Label>

              {canPickRemote ? (
                <div className="flex gap-2">
                  {/* Chọn từ danh sách thật, không gõ tên remote — tên phân biệt
                      hoa thường và gõ nhầm chỉ lộ ra lúc chạy job. */}
                  <select
                    aria-label="Remote"
                    value={remote}
                    onChange={(event) => setRemote(event.target.value)}
                    className="h-9 shrink-0 rounded-md border border-input bg-transparent px-2 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 dark:bg-input/30"
                  >
                    <option value="">Chọn remote…</option>
                    {remoteList.map((item) => (
                      <option key={item.name} value={item.name}>
                        {item.name} ({item.type})
                      </option>
                    ))}
                  </select>

                  <Input
                    id="job-destination"
                    value={destinationPath}
                    onChange={(event) => setDestinationPath(event.target.value)}
                    placeholder="HubBackup/tai-lieu"
                    autoComplete="off"
                    spellCheck={false}
                  />
                </div>
              ) : (
                <Input
                  id="job-destination"
                  value={destinationPath}
                  onChange={(event) => setDestinationPath(event.target.value)}
                  placeholder="hub:HubBackup/tai-lieu"
                  autoComplete="off"
                  spellCheck={false}
                />
              )}

              <p className="text-xs text-muted-foreground">
                {canPickRemote
                  ? 'Chọn remote rồi gõ đường dẫn bên trong nó. Remote crypt thì bật ổ khoá bên dưới.'
                  : 'Dạng remote:đường/dẫn của rclone — không đọc được danh sách remote nên phải gõ tay.'}
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

          {uploadFolder.isError ? (
            <Alert variant="destructive">
              <AlertDescription>{uploadFolder.error.message}</AlertDescription>
            </Alert>
          ) : null}

          {saveJob.isError ? (
            <Alert variant="destructive">
              <AlertDescription>{saveJob.error.message}</AlertDescription>
            </Alert>
          ) : null}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => handleOpenChange(false)}>
              Huỷ
            </Button>
            <Button type="submit" disabled={!canSubmit || busy}>
              {busy ? (
                <>
                  <Loader2 className="size-4 animate-spin" aria-hidden="true" />
                  {uploadFolder.isPending ? 'Đang tải lên' : 'Đang lưu'}
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

function ModeButton({
  active,
  onClick,
  icon,
  title,
  hint,
}: {
  active: boolean
  onClick: () => void
  icon: React.ReactNode
  title: string
  hint: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={
        'flex items-start gap-2 rounded-md border p-2.5 text-left text-sm ' +
        (active ? 'border-primary bg-accent/50' : 'hover:bg-accent/30')
      }
    >
      <span className="mt-0.5 shrink-0 text-muted-foreground">{icon}</span>
      <span className="min-w-0">
        <span className="block font-medium">{title}</span>
        <span className="block text-xs text-muted-foreground">{hint}</span>
      </span>
    </button>
  )
}

/**
 * Tiến trình theo BYTE, không theo số tệp: 3000 ảnh thumbnail và 3 video dài cho
 * cảm giác rất khác nhau nếu đếm theo tệp.
 */
function UploadBar({ progress }: { progress: UploadProgress }) {
  const percent = progress.totalBytes === 0
    ? 0
    : Math.round((progress.sentBytes / progress.totalBytes) * 100)

  return (
    <div className="space-y-1">
      <div className="h-1.5 overflow-hidden rounded-full bg-muted">
        <div className="h-full bg-primary transition-[width]" style={{ width: `${percent}%` }} />
      </div>
      <p className="text-xs text-muted-foreground">
        {percent}% · {formatBytes(progress.sentBytes)} / {formatBytes(progress.totalBytes)}
        {progress.duplicates > 0 ? ` · ${progress.duplicates} tệp đã có` : ''}
        {progress.rejected > 0 ? ` · ${progress.rejected} tệp bị bỏ qua` : ''}
      </p>
    </div>
  )
}

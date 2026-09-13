import { AlertCircle, CircleCheck, CircleX, Loader2, Lock, Plus, ServerOff, Trash2 } from 'lucide-react'
import { useState } from 'react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError } from '@/shared/api/client'
import {
  toNumber,
  useBackupHistory,
  useBackupStatus,
  useDeleteBackupJob,
  useRunBackupJob,
  type BackupJobDto,
  type BackupRunDto,
} from '@/shared/api/backup'
import { PageContainer } from '@/shared/components/PageContainer'

import { JobDialog } from './JobDialog'
import {
  describeRelativeTime,
  formatBytes,
  formatDuration,
  formatFileCount,
} from '@/shared/lib/time'

/**
 * Năng lực 3 — sao lưu lên cloud qua rclone.
 *
 * Câu hỏi thật của người dùng là "dữ liệu của tôi có an toàn không", nên dòng
 * đầu trang trả lời đúng câu đó: bao nhiêu công việc chưa xong, lần cuối chạy
 * khi nào. Tổng dung lượng đã sao lưu là con số hư danh — biết 3,2 GB không
 * cho biết đêm qua có chạy được không.
 *
 * KHÔNG có cây thư mục, và đó là cố ý: `BackupRun` chỉ giữ số lượng và dung
 * lượng vì §6.5 mục 4 cấm ghi đường dẫn từng tệp. `BackupJobDto` cũng không
 * trả `Source`/`Destination` — đường dẫn nằm lại phía máy chủ. Giao diện không
 * được gợi ý một loại dữ liệu mà hệ thống cố tình không lưu.
 */
export function BackupPage() {
  const status = useBackupStatus()
  const [dialogOpen, setDialogOpen] = useState(false)

  return (
    <PageContainer className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight lg:text-2xl">Sao lưu</h1>
          <Verdict data={status.data} isPending={status.isPending} />
        </div>

        <Button size="sm" onClick={() => setDialogOpen(true)}>
          <Plus className="size-4" aria-hidden="true" />
          Thêm thư mục
        </Button>
      </div>

      <JobDialog open={dialogOpen} onOpenChange={setDialogOpen} />

      {status.isPending ? <JobsPending /> : null}

      {status.isError ? <BackupError error={status.error} /> : null}

      {status.data ? <BackupBody data={status.data} /> : null}
    </PageContainer>
  )
}

function BackupBody({ data }: { data: NonNullable<ReturnType<typeof useBackupStatus>['data']> }) {
  // rclone thiếu là lỗi cấu hình của người vận hành, không phải lỗi tạm thời.
  // Nói ngay ở đầu, trước khi họ bấm "Chạy ngay" rồi mới thấy hỏng.
  const rcloneMissing = !data.rcloneAvailable

  return (
    <>
      {rcloneMissing ? (
        <Alert variant="destructive" className="lg:max-w-3xl">
          <ServerOff aria-hidden="true" />
          <AlertTitle>Không gọi được rclone</AlertTitle>
          <AlertDescription>
            <p>
              Hub không chạy được lệnh <code>rclone</code>, nên mọi công việc sao lưu sẽ thất bại.
            </p>
            <p>
              Cài rclone rồi khai đường dẫn tuyệt đối ở mục <code>Backup:RclonePath</code> — chạy
              như Windows Service thì PATH khác của bạn.
            </p>
          </AlertDescription>
        </Alert>
      ) : null}

      {data.jobs.length === 0 ? <NoJobs rcloneVersion={data.rcloneVersion} /> : null}

      {data.jobs.length > 0 ? (
        <ul className="space-y-3 lg:max-w-3xl">
          {data.jobs.map((job) => (
            <li key={job.name}>
              <JobCard job={job} />
            </li>
          ))}
        </ul>
      ) : null}

      {data.jobs.length > 0 ? <History /> : null}
    </>
  )
}

/**
 * Dòng kết luận. Phải nói thật: còn một công việc hỏng thì không được báo
 * "cả 3 xong" — người dùng tin dòng này và sẽ không cuộn xuống kiểm tra.
 */
function Verdict({
  data,
  isPending,
}: {
  data: ReturnType<typeof useBackupStatus>['data']
  isPending: boolean
}) {
  if (isPending || !data) {
    return (
      <p className="mt-1 text-sm text-muted-foreground">
        Công việc sao lưu lên cloud và lịch sử các lần chạy.
      </p>
    )
  }

  const jobs = data.jobs
  if (jobs.length === 0) {
    // Không lặp lại "Chưa có công việc sao lưu nào" ở đây — thẻ NoJobs bên dưới
    // đã nói đúng câu đó. Hai lần cùng một câu liền nhau đọc như lỗi hiển thị.
    return (
      <p className="mt-1 text-sm text-muted-foreground">
        Công việc sao lưu lên cloud và lịch sử các lần chạy.
      </p>
    )
  }

  const running = jobs.filter((job) => job.isRunning)
  const unfinished = jobs.filter((job) => !job.isRunning && job.latestRun?.status !== 'Succeeded')

  const latestFinish = jobs
    .map((job) => job.latestRun?.finishedAt)
    .filter((value): value is string => Boolean(value))
    .sort()
    .at(-1)

  if (running.length > 0) {
    return (
      <VerdictLine tone="running">
        {`Đang chạy ${running.map((job) => job.name).join(', ')}`}
      </VerdictLine>
    )
  }

  if (unfinished.length > 0) {
    return (
      <VerdictLine tone="bad">
        {`${unfinished.length} trong ${jobs.length} công việc chưa xong` +
          (latestFinish ? ` · lần cuối ${describeRelativeTime(latestFinish)}` : '')}
      </VerdictLine>
    )
  }

  return (
    <VerdictLine tone="good">
      {`Cả ${jobs.length} công việc đã xong` +
        (latestFinish ? ` · lần cuối ${describeRelativeTime(latestFinish)}` : '')}
    </VerdictLine>
  )
}

function VerdictLine({ tone, children }: { tone: 'good' | 'bad' | 'running'; children: string }) {
  const Icon = tone === 'good' ? CircleCheck : tone === 'bad' ? CircleX : Loader2

  return (
    // items-start, không items-center: khi câu xuống hai dòng trên điện thoại,
    // canh giữa làm biểu tượng trôi xuống giữa chừng như thuộc dòng dưới.
    <p className="mt-1 flex items-start gap-1.5 text-sm text-muted-foreground">
      <Icon
        className={
          'mt-0.5 size-3.5 shrink-0 ' +
          (tone === 'good'
            ? 'text-success'
            : tone === 'bad'
              ? 'text-destructive'
              : 'animate-spin text-muted-foreground')
        }
        aria-hidden="true"
      />
      <span>{children}</span>
    </p>
  )
}

function JobCard({ job }: { job: BackupJobDto }) {
  const runJob = useRunBackupJob()
  const deleteJob = useDeleteBackupJob()
  const run = job.latestRun

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <CardTitle className="text-base">{job.name}</CardTitle>
          {/* Ổ khoá chỉ xuất hiện khi đích đã mã hoá. Cờ này có trong DTO riêng
              cho giao diện — để thấy rõ dữ liệu nào được bảo vệ. */}
          {job.encrypted ? (
            <Lock className="size-3.5 shrink-0 text-muted-foreground" aria-label="Đã mã hoá" />
          ) : null}
          <span className="ms-auto">
            <RunStatus job={job} />
          </span>
        </div>
      </CardHeader>

      <CardContent className="space-y-3 text-sm">
        {/* Hai cờ quyết định mức an toàn: mã hoá hay không, và xoá tệp thừa ở
            đích hay chỉ thêm. `sync` nghĩa là xoá nhầm ở máy sẽ lan lên cloud,
            nên nói thẳng thay vì giấu sau một biểu tượng. */}
        <p className={job.deleteExtra ? 'font-medium text-foreground' : 'text-muted-foreground'}>
          {job.encrypted ? 'Đã mã hoá' : 'Nguyên bản'}
          {' · '}
          {job.deleteExtra ? 'xoá tệp thừa ở đích (sync)' : 'chỉ thêm, không xoá (copy)'}
        </p>

        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="text-muted-foreground">{describeRun(run)}</span>

          <span className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={job.isRunning || runJob.isPending}
              onClick={() => runJob.mutate(job.name)}
            >
              {job.isRunning ? 'Đang chạy' : 'Chạy ngay'}
            </Button>

            {/* Chỉ xoá khỏi danh sách công việc — KHÔNG đụng tới tệp đã sao lưu
                trên cloud, cũng không xoá .backupignore trong thư mục người dùng. */}
            <Button
              variant="ghost"
              size="icon-sm"
              disabled={job.isRunning || deleteJob.isPending}
              onClick={() => {
                if (confirm(`Xoá công việc "${job.name}"? Tệp đã sao lưu trên cloud vẫn còn.`)) {
                  deleteJob.mutate(job.name)
                }
              }}
              aria-label={`Xoá ${job.name}`}
            >
              <Trash2 className="size-3.5" aria-hidden="true" />
            </Button>
          </span>
        </div>

        {/* ErrorMessage cố tình là câu chung, không đường dẫn không stack trace
            (§6.5 mục 7) — hiển thị nguyên văn là đủ và đúng. */}
        {run?.status === 'Failed' && run.errorMessage ? (
          <p className="text-destructive">{run.errorMessage}</p>
        ) : null}

        {/* "Cancelled" sinh ra khi hub chết giữa chừng hoặc quá thời hạn — không
            người dùng nào bấm huỷ. Viết "Đã huỷ" là đổ lỗi cho họ. */}
        {run?.status === 'Cancelled' ? (
          <p className="text-destructive">
            {run.errorMessage ?? 'Máy tắt giữa chừng.'} Chạy lại để tiếp tục.
          </p>
        ) : null}

        {runJob.isError ? <p className="text-destructive">{runJob.error.message}</p> : null}
        {deleteJob.isError ? (
          <p className="text-destructive">{deleteJob.error.message}</p>
        ) : null}
      </CardContent>
    </Card>
  )
}

function RunStatus({ job }: { job: BackupJobDto }) {
  if (job.isRunning) {
    return (
      <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Loader2 className="size-3 animate-spin" aria-hidden="true" />
        Đang chạy
      </span>
    )
  }

  const run = job.latestRun
  if (!run) {
    return <span className="text-xs text-muted-foreground">Chưa từng chạy</span>
  }

  const ok = run.status === 'Succeeded'

  return (
    <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
      {ok ? (
        <CircleCheck className="size-3 text-success" aria-hidden="true" />
      ) : (
        <CircleX className="size-3 text-destructive" aria-hidden="true" />
      )}
      {describeRelativeTime(run.finishedAt ?? run.startedAt)}
    </span>
  )
}

/** Số liệu một lần chạy. Chỉ số lượng và dung lượng — không có tên tệp. */
function describeRun(run: BackupRunDto | null): string {
  if (!run) return 'Chưa có lần chạy nào.'

  const parts = [
    formatFileCount(toNumber(run.filesTransferred)),
    formatBytes(toNumber(run.bytesTransferred)),
  ]
  const duration = formatDuration(run.startedAt, run.finishedAt)
  if (duration) parts.push(duration)

  return parts.join(' · ')
}

function History() {
  const history = useBackupHistory()

  if (history.isPending) {
    return (
      <section className="space-y-2 lg:max-w-3xl" aria-busy="true">
        <h2 className="text-sm font-semibold">Lịch sử</h2>
        <Skeleton className="h-24 w-full" />
      </section>
    )
  }

  if (history.isError || !history.data || history.data.length === 0) {
    return null
  }

  return (
    <section className="space-y-1 lg:max-w-3xl">
      <h2 className="text-sm font-semibold">Lịch sử</h2>
      {/* Nói thẳng ra rằng không có tên tệp: người dùng đi tìm sẽ không thấy,
          và tưởng giao diện thiếu chứ không biết là cố ý (§6.5 mục 4). */}
      <p className="text-xs text-muted-foreground">
        Chỉ lưu số lượng và dung lượng — không lưu tên tệp.
      </p>

      <ul className="mt-2 divide-y divide-border text-sm">
        {history.data.map((run) => (
          <li key={run.id} className="flex items-center gap-3 py-2.5">
            {run.status === 'Succeeded' ? (
              <CircleCheck className="size-3.5 shrink-0 text-success" aria-hidden="true" />
            ) : (
              <CircleX className="size-3.5 shrink-0 text-destructive" aria-hidden="true" />
            )}
            <span className="w-24 shrink-0 truncate font-medium">{run.jobName}</span>
            <span className="flex-1 truncate text-muted-foreground">
              {describeRelativeTime(run.finishedAt ?? run.startedAt)}
            </span>
            {/* tabular-nums: cột số phải thẳng hàng để đọc dọc mà không dò từng dòng. */}
            <span className="w-20 shrink-0 text-right tabular-nums text-muted-foreground">
              {formatDuration(run.startedAt, run.finishedAt) ?? '—'}
            </span>
            <span className="w-20 shrink-0 text-right tabular-nums text-muted-foreground">
              {run.status === 'Succeeded' ? formatBytes(toNumber(run.bytesTransferred)) : '—'}
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}

/** Chưa khai job nào — màn hình trống là lời mời hành động, không phải lỗi. */
function NoJobs({ rcloneVersion }: { rcloneVersion: string | null }) {
  return (
    <Card className="lg:max-w-3xl">
      <CardContent className="space-y-2 py-6 text-sm">
        <p className="font-medium">Chưa có công việc sao lưu nào.</p>
        <p className="text-muted-foreground">
          Mỗi công việc là một cặp thư mục trên máy này và đích trên cloud. Bấm{' '}
          <span className="font-medium text-foreground">Thêm thư mục</span> để tạo, hoặc khai ở mục{' '}
          <code>Backup:Jobs</code> trong cấu hình hub.
        </p>
        {rcloneVersion ? <p className="text-success">{rcloneVersion} · sẵn sàng</p> : null}
      </CardContent>
    </Card>
  )
}

function BackupError({ error }: { error: Error }) {
  const status = error instanceof ApiError ? error.status : 0

  return (
    <Alert variant="destructive" className="lg:max-w-3xl">
      <AlertCircle aria-hidden="true" />
      <AlertTitle>
        {status === 503 ? 'Chưa cấu hình sao lưu' : 'Không đọc được trạng thái sao lưu'}
      </AlertTitle>
      <AlertDescription>
        <p>{error.message}</p>
      </AlertDescription>
    </Alert>
  )
}

function JobsPending() {
  return (
    <ul className="space-y-3 lg:max-w-3xl" aria-busy="true">
      {[0, 1].map((index) => (
        <li key={index}>
          <Card>
            <CardHeader>
              <Skeleton className="h-5 w-32" />
            </CardHeader>
            <CardContent className="space-y-3">
              <Skeleton className="h-4 w-2/3" />
              <Skeleton className="h-4 w-1/2" />
            </CardContent>
          </Card>
        </li>
      ))}
    </ul>
  )
}

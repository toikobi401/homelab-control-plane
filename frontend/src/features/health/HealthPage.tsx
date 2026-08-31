import { AlertCircle, CheckCircle2, RefreshCw } from 'lucide-react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useHealth } from '@/shared/api/health'
import { PageContainer } from '@/shared/components/PageContainer'

/**
 * Trạng thái hub — màn hình chính ở Phase 0.
 *
 * Đây là màn hình để kiểm chứng ô cuối của Phase 0: mở được giao diện trên iPhone
 * và Android qua tailnet và thấy trạng thái /health (CONTEXT.md §10).
 */
export function HealthPage() {
  const { data, error, isPending, isError, isFetching, refetch } = useHealth()

  return (
    <PageContainer className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold tracking-tight lg:text-2xl">Trạng thái hub</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Kiểm tra backend còn phục vụ được từ thiết bị này.
        </p>
      </div>

      {isPending ? <HealthPending /> : isError ? <HealthError error={error} /> : null}

      {data ? (
        <Card className="lg:max-w-2xl">
          <CardHeader>
            <div className="flex items-center gap-2">
              <CheckCircle2 className="size-5 shrink-0 text-success" aria-hidden="true" />
              <CardTitle>Hub đang hoạt động</CardTitle>
            </div>
            <CardDescription>Backend trả lời bình thường.</CardDescription>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-3 text-sm">
              <div className="flex items-baseline justify-between gap-4">
                <dt className="text-muted-foreground">Trạng thái</dt>
                <dd className="font-medium">{data.status}</dd>
              </div>
              <div className="flex items-baseline justify-between gap-4">
                <dt className="text-muted-foreground">Máy chủ báo lúc</dt>
                <dd className="text-right font-medium tabular-nums">{formatTime(data.utc)}</dd>
              </div>
            </dl>
          </CardContent>
        </Card>
      ) : null}

      {/* Một nút duy nhất trong DOM: render hai bản rồi ẩn bớt bằng CSS thì
          trình đọc màn hình vẫn đọc cả hai. Điện thoại cần full-width để ngón
          cái bấm trúng; desktop dùng chuột nên nút vừa nội dung là đủ. */}
      <RefreshButton
        isFetching={isFetching}
        onRefetch={() => void refetch()}
        className="w-full lg:w-auto"
      />
    </PageContainer>
  )
}

interface RefreshButtonProps {
  isFetching: boolean
  onRefetch: () => void
  className?: string
}

function RefreshButton({ isFetching, onRefetch, className }: RefreshButtonProps) {
  return (
    <Button variant="outline" className={className} onClick={onRefetch} disabled={isFetching}>
      <RefreshCw className={isFetching ? 'animate-spin' : undefined} aria-hidden="true" />
      {isFetching ? 'Đang kiểm tra…' : 'Kiểm tra lại'}
    </Button>
  )
}

function HealthPending() {
  return (
    <Card aria-busy="true" className="lg:max-w-2xl">
      <CardHeader>
        <Skeleton className="h-5 w-40" />
        <Skeleton className="h-4 w-56" />
      </CardHeader>
      <CardContent className="space-y-3">
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-2/3" />
      </CardContent>
    </Card>
  )
}

function HealthError({ error }: { error: Error }) {
  return (
    <Alert variant="destructive" className="lg:max-w-2xl">
      <AlertCircle aria-hidden="true" />
      <AlertTitle>Không liên lạc được với hub</AlertTitle>
      <AlertDescription>
        <p>{error.message}</p>
        <p>Kiểm tra Tailscale trên thiết bị này còn kết nối không, và backend còn chạy không.</p>
      </AlertDescription>
    </Alert>
  )
}

/**
 * `utc` từ backend là chuỗi ISO. Hiện theo múi giờ của thiết bị — người dùng quan
 * tâm "lúc nãy" theo giờ của họ, không phải theo UTC.
 */
function formatTime(utc: string): string {
  const parsed = new Date(utc)
  if (Number.isNaN(parsed.getTime())) {
    return utc
  }

  return parsed.toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'medium' })
}

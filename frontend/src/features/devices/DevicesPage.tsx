import { AlertCircle, RefreshCw, ServerOff, TriangleAlert } from 'lucide-react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError } from '@/shared/api/client'
import { useDevices, type DeviceDto } from '@/shared/api/devices'

import { DeviceIcon } from './DeviceIcon'

/**
 * Năng lực 1 — thiết bị trong tailnet và trạng thái hiện diện.
 *
 * Lưu ý quan trọng về `isOnline`: Tailscale KHÔNG trả trường "online" trong API
 * danh sách thiết bị. Backend suy nó ra từ `lastSeen` theo ngưỡng 5 phút. Vì vậy
 * giao diện nói "thấy 2 phút trước", không khẳng định "máy đang bật" — trình bày
 * một phỏng đoán như sự thật là nói dối người dùng.
 */
export function DevicesPage() {
  const { data, error, isPending, isError, isFetching, refetch } = useDevices()

  return (
    <div className="space-y-4">
      <div className="lg:flex lg:items-start lg:justify-between lg:gap-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight lg:text-2xl">Thiết bị</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {data
              ? `${data.devices.length} thiết bị trong tailnet, ${data.onlineCount} vừa hoạt động.`
              : 'Thiết bị trong tailnet và lần cuối chúng còn hoạt động.'}
          </p>
        </div>
      </div>

      {isPending ? <DevicesPending /> : null}

      {isError ? <DevicesError error={error} /> : null}

      {data ? (
        data.devices.length === 0 ? (
          <Card className="lg:max-w-2xl">
            <CardContent className="py-8 text-center text-sm text-muted-foreground">
              Tailnet chưa có thiết bị nào.
            </CardContent>
          </Card>
        ) : (
          <ul className="space-y-3 lg:max-w-2xl">
            {data.devices.map((device) => (
              <li key={device.id}>
                <DeviceCard device={device} />
              </li>
            ))}
          </ul>
        )
      ) : null}

      {!isPending ? (
        <Button
          variant="outline"
          className="w-full lg:w-auto"
          onClick={() => void refetch()}
          disabled={isFetching}
        >
          <RefreshCw className={isFetching ? 'animate-spin' : undefined} aria-hidden="true" />
          {isFetching ? 'Đang cập nhật…' : 'Cập nhật'}
        </Button>
      ) : null}
    </div>
  )
}

function DeviceCard({ device }: { device: DeviceDto }) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <DeviceIcon
                operatingSystem={device.operatingSystem}
                className="size-4 shrink-0 text-muted-foreground"
              />
              <CardTitle className="text-base">{device.hostname}</CardTitle>
              <PresenceBadge device={device} />
            </div>
            <CardDescription className="mt-1 break-all">{device.name}</CardDescription>
          </div>
        </div>
      </CardHeader>

      <CardContent>
        <dl className="grid gap-2 text-sm">
          <Row label="Hệ điều hành" value={device.operatingSystem} />
          <Row label="Địa chỉ tailnet" value={device.tailnetAddress ?? 'Chỉ có IPv6'} />
          <Row label="Thấy lần cuối" value={describeLastSeen(device.lastSeen)} />
          {device.clientVersion ? (
            <Row label="Phiên bản Tailscale" value={device.clientVersion} />
          ) : null}
        </dl>

        {/* Cảnh báo vận hành. Thiết bị chưa duyệt không vào được tailnet, còn
            thiết bị ngoài là của tài khoản khác chia sẻ vào — cả hai đều bất
            thường với hệ thống một người dùng, nên phải nói ra. */}
        {!device.authorized || device.isExternal || device.updateAvailable ? (
          <ul className="mt-3 space-y-1 text-sm">
            {!device.authorized ? <Warning>Chưa được duyệt vào tailnet.</Warning> : null}
            {device.isExternal ? <Warning>Được chia sẻ từ tài khoản khác.</Warning> : null}
            {device.updateAvailable ? <Warning>Có bản cập nhật Tailscale.</Warning> : null}
          </ul>
        ) : null}
      </CardContent>
    </Card>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="shrink-0 text-muted-foreground">{label}</dt>
      <dd className="text-right break-all">{value}</dd>
    </div>
  )
}

function Warning({ children }: { children: string }) {
  return (
    <li className="flex items-start gap-2 text-muted-foreground">
      <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
      <span>{children}</span>
    </li>
  )
}

/**
 * Nhãn hiện diện. Chữ "Vừa hoạt động" chứ không phải "Trực tuyến": backend suy
 * trạng thái này từ `lastSeen`, nó không biết máy có thật sự đang bật hay không.
 */
function PresenceBadge({ device }: { device: DeviceDto }) {
  return device.isOnline ? (
    <Badge variant="secondary" className="gap-1.5">
      <span className="size-1.5 rounded-full bg-success" aria-hidden="true" />
      Vừa hoạt động
    </Badge>
  ) : (
    <Badge variant="outline" className="gap-1.5 text-muted-foreground">
      <span className="size-1.5 rounded-full bg-muted-foreground" aria-hidden="true" />
      Không thấy
    </Badge>
  )
}

function DevicesError({ error }: { error: Error }) {
  // Backend phân biệt sẵn ba tình huống bằng mã HTTP — 503 là chưa cấu hình
  // (việc của người dùng), 502 là Tailscale trục trặc (đợi rồi thử lại). Gộp
  // chung thành "lỗi" sẽ khiến người dùng đi sai hướng khắc phục.
  const status = error instanceof ApiError ? error.status : 0

  if (status === 503) {
    return (
      <Alert className="lg:max-w-2xl">
        <ServerOff aria-hidden="true" />
        <AlertTitle>Chưa cấu hình Tailscale</AlertTitle>
        <AlertDescription>
          <p>{error.message}</p>
          <p>
            Cần OAuth client ID và secret từ admin console của Tailscale, khai qua User Secrets lúc
            phát triển hoặc biến môi trường lúc chạy thật.
          </p>
        </AlertDescription>
      </Alert>
    )
  }

  return (
    <Alert variant="destructive" className="lg:max-w-2xl">
      <AlertCircle aria-hidden="true" />
      <AlertTitle>
        {status === 502 ? 'Không gọi được Tailscale' : 'Không đọc được danh sách thiết bị'}
      </AlertTitle>
      <AlertDescription>
        <p>{error.message}</p>
        {status === 502 ? <p>Tailscale có thể đang trục trặc — thử lại sau ít phút.</p> : null}
      </AlertDescription>
    </Alert>
  )
}

function DevicesPending() {
  return (
    <ul className="space-y-3 lg:max-w-2xl" aria-busy="true">
      {[0, 1].map((index) => (
        <li key={index}>
          <Card>
            <CardHeader>
              <Skeleton className="h-5 w-40" />
              <Skeleton className="h-4 w-56" />
            </CardHeader>
            <CardContent className="space-y-3">
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-2/3" />
            </CardContent>
          </Card>
        </li>
      ))}
    </ul>
  )
}

/**
 * `lastSeen` nói lên nhiều hơn cờ online/offline: "2 phút trước" và "3 ngày
 * trước" đều là "không thấy", nhưng ý nghĩa hoàn toàn khác nhau.
 */
function describeLastSeen(lastSeen: string | null): string {
  if (!lastSeen) {
    return 'Chưa từng thấy'
  }

  const parsed = new Date(lastSeen)
  if (Number.isNaN(parsed.getTime())) {
    return lastSeen
  }

  const seconds = Math.floor((Date.now() - parsed.getTime()) / 1000)

  if (seconds < 60) return 'Vài giây trước'
  if (seconds < 3600) return `${Math.floor(seconds / 60)} phút trước`
  if (seconds < 86_400) return `${Math.floor(seconds / 3600)} giờ trước`
  if (seconds < 2_592_000) return `${Math.floor(seconds / 86_400)} ngày trước`

  return parsed.toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' })
}

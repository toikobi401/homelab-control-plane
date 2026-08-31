import { AlertCircle, LogOut, Monitor, ShieldOff, Trash2 } from 'lucide-react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError } from '@/shared/api/client'
import {
  useLogout,
  useRevokeAllSessions,
  useRevokeSession,
  useSessions,
  type SessionDto,
} from '@/shared/api/auth'
import { PageContainer } from '@/shared/components/PageContainer'

/**
 * Phiên đăng nhập đang mở (§6.3).
 *
 * Đây là màn hình thực thi yêu cầu "đăng xuất tất cả thiết bị" — điều kiện sống
 * còn khi mất điện thoại. Phiên nằm trong DB nên thu hồi có hiệu lực ngay.
 */
export function SessionsPage() {
  const { data, error, isPending, isError } = useSessions()
  const logout = useLogout()
  const revokeAll = useRevokeAllSessions()

  return (
    <PageContainer className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold tracking-tight lg:text-2xl">Phiên đăng nhập</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Thiết bị đang mở hub. Thu hồi có hiệu lực ngay, không đợi hết hạn.
        </p>
      </div>

      {isPending ? <SessionsPending /> : null}

      {/* 401 ở đây nghĩa là phiên vừa chết (hết hạn, hoặc chính người dùng vừa
          thu hồi tất cả). AuthGate sẽ đưa về màn hình đăng nhập ngay sau đó —
          hiện lỗi đỏ trong khoảnh khắc chuyển tiếp chỉ làm người dùng hoảng. */}
      {isError && !isUnauthorized(error) ? (
        <Alert variant="destructive" className="lg:max-w-2xl">
          <AlertCircle aria-hidden="true" />
          <AlertTitle>Không đọc được danh sách phiên</AlertTitle>
          <AlertDescription>{error.message}</AlertDescription>
        </Alert>
      ) : null}

      {data ? (
        data.length === 0 ? (
          // Trạng thái rỗng (§7). Trên thực tế khó xảy ra vì chính người đang xem
          // cũng là một phiên — nhưng bỏ sót nó là bỏ sót một trạng thái.
          <Card className="lg:max-w-2xl">
            <CardContent className="py-8 text-center text-sm text-muted-foreground">
              Không có phiên nào đang mở.
            </CardContent>
          </Card>
        ) : (
          <ul className="space-y-3 lg:max-w-2xl">
            {data.map((session) => (
              <li key={session.id}>
                <SessionCard session={session} />
              </li>
            ))}
          </ul>
        )
      ) : null}

      {data && data.length > 0 ? (
        <div className="flex flex-col gap-2 lg:max-w-2xl lg:flex-row">
          <Button
            variant="outline"
            className="w-full lg:w-auto"
            onClick={() => logout.mutate()}
            disabled={logout.isPending}
          >
            <LogOut aria-hidden="true" />
            Đăng xuất thiết bị này
          </Button>

          <Button
            variant="destructive"
            className="w-full lg:w-auto"
            onClick={() => revokeAll.mutate(false)}
            disabled={revokeAll.isPending}
          >
            <ShieldOff aria-hidden="true" />
            {revokeAll.isPending ? 'Đang thu hồi…' : 'Đăng xuất tất cả thiết bị'}
          </Button>
        </div>
      ) : null}

      {revokeAll.isError ? (
        <Alert variant="destructive" className="lg:max-w-2xl">
          <AlertCircle aria-hidden="true" />
          <AlertTitle>Không thu hồi được</AlertTitle>
          <AlertDescription>{revokeAll.error.message}</AlertDescription>
        </Alert>
      ) : null}
    </PageContainer>
  )
}

function SessionCard({ session }: { session: SessionDto }) {
  const revoke = useRevokeSession()

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <Monitor className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
              <CardTitle className="text-base">{describeDevice(session.device)}</CardTitle>
              {session.isCurrent ? <Badge variant="secondary">Thiết bị này</Badge> : null}
            </div>
            <CardDescription className="mt-1">
              {session.tailnetAddress ?? 'Không rõ địa chỉ'}
            </CardDescription>
          </div>

          <Button
            variant="ghost"
            size="icon"
            onClick={() => revoke.mutate(session.id)}
            disabled={revoke.isPending}
            aria-label={`Thu hồi phiên ${describeDevice(session.device)}`}
          >
            <Trash2 aria-hidden="true" />
          </Button>
        </div>
      </CardHeader>

      <CardContent>
        <dl className="grid gap-2 text-sm">
          <div className="flex items-baseline justify-between gap-4">
            <dt className="text-muted-foreground">Đăng nhập lúc</dt>
            <dd className="text-right tabular-nums">{formatTime(session.createdAt)}</dd>
          </div>
          <div className="flex items-baseline justify-between gap-4">
            <dt className="text-muted-foreground">Hoạt động gần nhất</dt>
            <dd className="text-right tabular-nums">{formatTime(session.lastSeenAt)}</dd>
          </div>
          <div className="flex items-baseline justify-between gap-4">
            <dt className="text-muted-foreground">Hết hạn</dt>
            <dd className="text-right tabular-nums">{formatTime(session.expiresAt)}</dd>
          </div>
        </dl>

        {revoke.isError ? (
          <p className="mt-3 text-sm text-destructive">{revoke.error.message}</p>
        ) : null}
      </CardContent>
    </Card>
  )
}

function isUnauthorized(error: Error | null): boolean {
  return error instanceof ApiError && error.isUnauthorized
}

function SessionsPending() {
  return (
    <Card aria-busy="true" className="lg:max-w-2xl">
      <CardHeader>
        <Skeleton className="h-5 w-48" />
        <Skeleton className="h-4 w-32" />
      </CardHeader>
      <CardContent className="space-y-3">
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-2/3" />
      </CardContent>
    </Card>
  )
}

/**
 * `device` là chuỗi User-Agent thô, dài và khó đọc. Rút gọn thành tên trình duyệt
 * và hệ điều hành — đủ để nhận ra "cái này là điện thoại của mình" hay không.
 */
function describeDevice(userAgent: string): string {
  const browser = /Edg\//.test(userAgent)
    ? 'Edge'
    : /OPR\//.test(userAgent)
      ? 'Opera'
      : /Firefox\//.test(userAgent)
        ? 'Firefox'
        : /Chrome\//.test(userAgent)
          ? 'Chrome'
          : /Safari\//.test(userAgent)
            ? 'Safari'
            : null

  const platform = /iPhone|iPad/.test(userAgent)
    ? 'iOS'
    : /Android/.test(userAgent)
      ? 'Android'
      : /Mac OS X/.test(userAgent)
        ? 'macOS'
        : /Windows/.test(userAgent)
          ? 'Windows'
          : /Linux/.test(userAgent)
            ? 'Linux'
            : null

  if (browser && platform) {
    return `${browser} trên ${platform}`
  }

  // Không nhận ra được thì hiện nguyên bản đã cắt ngắn, còn hơn nói "Không rõ".
  return userAgent.length > 60 ? `${userAgent.slice(0, 60)}…` : userAgent
}

function formatTime(value: string): string {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return value
  }

  return parsed.toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' })
}

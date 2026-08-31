import {
  AlertCircle,
  ExternalLink,
  Maximize2,
  MonitorSmartphone,
  RefreshCw,
  Settings2,
  ShieldAlert,
} from 'lucide-react'
import { useRef, useState } from 'react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useMeshCentralConfig } from '@/shared/api/meshCentral'
import { PageContainer } from '@/shared/components/PageContainer'

/**
 * Năng lực 6 — điều khiển thiết bị từ xa, nhúng MeshCentral.
 *
 * §2.3: "tái sử dụng giao thức, đừng phát minh lại". MeshCentral lo agent đa nền
 * tảng, điều khiển nguồn, Wake-on-LAN, và remote desktop. Hub giữ vai control
 * plane: đăng nhập (§6), điều hướng, và các năng lực khác.
 *
 * Trang này khác mọi trang khác ở một điểm: nội dung chính là **iframe cần càng
 * nhiều chỗ càng tốt**, nên nó thoát khỏi bề rộng đọc được của `PageContainer`
 * và chiếm hết chiều cao còn lại của viewport.
 */
export function RemotePage() {
  const { data, isPending, isError, refetch } = useMeshCentralConfig()

  if (isPending) {
    return (
      <PageContainer className="space-y-4">
        <PageHeading />
        <Skeleton className="h-96 w-full rounded-lg" />
      </PageContainer>
    )
  }

  if (isError) {
    return (
      <PageContainer className="space-y-4">
        <PageHeading />
        <Alert variant="destructive">
          <AlertCircle aria-hidden="true" />
          <AlertTitle>Không đọc được cấu hình</AlertTitle>
          <AlertDescription>
            Không hỏi được backend về địa chỉ MeshCentral. Kiểm tra backend còn chạy không.
          </AlertDescription>
        </Alert>
      </PageContainer>
    )
  }

  if (!data.configured || !data.url) {
    return (
      <PageContainer className="space-y-4">
        <PageHeading />
        <SetupGuide />
      </PageContainer>
    )
  }

  return <MeshCentralWorkspace url={data.url} onRetry={() => void refetch()} />
}

function PageHeading() {
  return (
    <div>
      <h1 className="text-xl font-semibold tracking-tight lg:text-2xl">Điều khiển máy</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Tắt, khởi động lại, đánh thức, và điều khiển màn hình máy đã cài agent.
      </p>
    </div>
  )
}

/**
 * Bố cục toàn màn hình cho MeshCentral.
 *
 * Thanh công cụ mỏng thay vì tiêu đề + mô tả đầy đủ: mỗi dòng chữ ở đây là một
 * dòng bị lấy khỏi iframe. Tên trang đã có ở điều hướng, người dùng biết mình
 * đang ở đâu.
 */
function MeshCentralWorkspace({ url, onRetry }: { url: string; onRetry: () => void }) {
  const frameRef = useRef<HTMLIFrameElement>(null)
  const [reloadKey, setReloadKey] = useState(0)

  function requestFullscreen() {
    // Toàn màn hình thật, không phải phóng to trong trang: điều khiển màn hình
    // máy khác cần từng pixel. Trình duyệt nào chặn thì bỏ qua trong im lặng —
    // nút vẫn còn đó, chỉ không có tác dụng, không đáng dựng thông báo lỗi.
    void frameRef.current?.requestFullscreen?.().catch(() => {})
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex min-w-0 items-center gap-2">
          <MonitorSmartphone className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <h1 className="truncate text-base font-semibold tracking-tight">MeshCentral</h1>
        </div>

        <div className="flex items-center gap-1">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setReloadKey((key) => key + 1)}
            title="Tải lại khung MeshCentral"
          >
            <RefreshCw className="size-4" aria-hidden="true" />
            <span className="sr-only lg:not-sr-only">Tải lại</span>
          </Button>

          <Button variant="ghost" size="sm" onClick={requestFullscreen} title="Toàn màn hình">
            <Maximize2 className="size-4" aria-hidden="true" />
            <span className="sr-only lg:not-sr-only">Toàn màn hình</span>
          </Button>

          {/* Mở tab mới là lối thoát khi iframe không chạy — nhất là lần đầu,
              khi trình duyệt chưa tin chứng chỉ tự ký của MeshCentral. */}
          <Button variant="outline" size="sm" asChild>
            <a href={url} target="_blank" rel="noopener noreferrer">
              <ExternalLink className="size-4" aria-hidden="true" />
              Mở tab mới
            </a>
          </Button>
        </div>
      </div>

      <MeshCentralFrame key={reloadKey} ref={frameRef} url={url} onRetry={onRetry} />
    </div>
  )
}

function MeshCentralFrame({
  ref,
  url,
  onRetry,
}: {
  ref: React.Ref<HTMLIFrameElement>
  url: string
  onRetry: () => void
}) {
  // Không có cách nào biết iframe tải được hay không: same-origin chặn ta đọc
  // nội dung, và sự kiện `load` không bắn khi trình duyệt chặn vì chứng chỉ.
  //
  // Nên không đoán — người dùng tự nói. Dòng nhắc nhỏ hiện thường trực; ai gặp
  // khung trắng thì mở panel hướng dẫn đầy đủ. Cách này không phiền người mà
  // mọi thứ đang chạy, và vẫn cứu được người bị chặn.
  const [showHelp, setShowHelp] = useState(false)

  return (
    <div className="relative flex min-h-0 flex-1 flex-col gap-1.5">
      {showHelp ? (
        <CertHelpOverlay url={url} onRetry={onRetry} onClose={() => setShowHelp(false)} />
      ) : null}

      <iframe
        ref={ref}
        src={url}
        title="MeshCentral"
        // min-h-[28rem]: trên điện thoại, chiều cao còn lại sau header và thanh
        // điều hướng quá ít để dùng được — thà cho cuộn trang còn hơn iframe dẹp.
        className="min-h-[28rem] w-full flex-1 rounded-lg border bg-background"
        // allow-same-origin + allow-scripts cùng lúc khiến trang nhúng tự gỡ
        // được sandbox. Chấp nhận ở đây vì MeshCentral là phần mềm ta tự host,
        // không phải nội dung lạ — và nó CẦN cả hai để chạy (WebSocket, WebRTC).
        // Bù lại, MeshCentral khai `frame-ancestors` chỉ cho origin của hub.
        sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-downloads allow-modals"
        allow="clipboard-read; clipboard-write; fullscreen"
      />

      <FrameFallbackNote onOpenHelp={() => setShowHelp(true)} />
    </div>
  )
}

/**
 * Lớp phủ hướng dẫn khi khung không hiện.
 *
 * Nguyên nhân hay gặp nhất là chứng chỉ tự ký: trình duyệt **không hỏi** bên
 * trong iframe, nó chỉ vẽ khung trống — người dùng không có cách nào đoán ra.
 * Nên nói thẳng nguyên nhân và đưa đúng hai nút cần bấm, theo thứ tự.
 */
function CertHelpOverlay({
  url,
  onRetry,
  onClose,
}: {
  url: string
  onRetry: () => void
  onClose: () => void
}) {
  return (
    <div
      className="absolute inset-x-0 top-0 bottom-6 z-10 flex items-center justify-center rounded-lg border bg-card/95 px-6 backdrop-blur"
      role="dialog"
      aria-label="Khung MeshCentral không hiện"
    >
      <div className="flex max-w-md flex-col items-center gap-4 text-center">
        <ShieldAlert className="size-10 text-muted-foreground" aria-hidden="true" />

        <div className="space-y-2">
          <h2 className="text-lg font-semibold tracking-tight">Trình duyệt chưa tin MeshCentral</h2>
          <p className="text-sm text-muted-foreground">
            MeshCentral dùng chứng chỉ tự ký. Trình duyệt không hỏi bên trong khung nhúng — nó chỉ
            hiện trống. Mở ở tab riêng một lần để chấp nhận chứng chỉ, rồi quay lại đây.
          </p>
        </div>

        <div className="flex flex-wrap items-center justify-center gap-2">
          <Button asChild>
            <a href={url} target="_blank" rel="noopener noreferrer">
              <ExternalLink className="size-4" aria-hidden="true" />
              Mở MeshCentral ở tab mới
            </a>
          </Button>

          <Button
            variant="outline"
            onClick={() => {
              onRetry()
              onClose()
            }}
          >
            <RefreshCw className="size-4" aria-hidden="true" />
            Thử lại
          </Button>
        </div>

        <Button variant="ghost" size="sm" onClick={onClose} className="text-muted-foreground">
          Đóng
        </Button>
      </div>
    </div>
  )
}

/**
 * Dòng nhắc thường trực dưới khung — cửa vào hướng dẫn khắc phục.
 *
 * Đã thử cách "tự phát hiện iframe hỏng rồi mới hiện" và bỏ: same-origin chặn
 * ta đọc nội dung bên trong, và sự kiện `load` **không bắn** khi trình duyệt
 * chặn vì chứng chỉ — đúng lúc cần nhất thì nó không hiện. Một dòng nhỏ luôn
 * có mặt trung thực hơn một phát hiện không đáng tin.
 */
function FrameFallbackNote({ onOpenHelp }: { onOpenHelp: () => void }) {
  return (
    <p className="text-center text-xs text-muted-foreground">
      Khung trống?{' '}
      <button
        type="button"
        onClick={onOpenHelp}
        className="underline underline-offset-2 hover:text-foreground"
      >
        Xem cách khắc phục
      </button>
    </p>
  )
}

/**
 * Chưa cấu hình thì hướng dẫn cụ thể, không để iframe trắng.
 *
 * Đây là màn hình người vận hành gặp lần đầu — nói thẳng phải làm gì thay vì
 * để họ đoán.
 */
function SetupGuide() {
  return (
    <div className="space-y-4">
      <Alert>
        <MonitorSmartphone className="size-4" aria-hidden="true" />
        <AlertTitle>Chưa cấu hình MeshCentral</AlertTitle>
        <AlertDescription className="space-y-3">
          <p>
            Điều khiển thiết bị dùng <strong>MeshCentral</strong> — công cụ mã nguồn mở lo phần
            agent, điều khiển nguồn, đánh thức máy, và điều khiển màn hình.
          </p>

          <ol className="ml-4 list-decimal space-y-1 text-sm">
            <li>
              Cài và chạy MeshCentral trên máy này — xem{' '}
              <code className="rounded bg-muted px-1 py-0.5">docs/meshcentral-setup.md</code>
            </li>
            <li>
              Khai địa chỉ của nó cho hub:{' '}
              <code className="rounded bg-muted px-1 py-0.5">MeshCentral:Url</code>
            </li>
            <li>Khởi động lại backend</li>
          </ol>

          <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
            <Settings2 className="size-3.5 shrink-0" aria-hidden="true" />
            Địa chỉ phải là địa chỉ tailnet mà điện thoại gọi tới được, không phải localhost.
          </p>
        </AlertDescription>
      </Alert>

      {/* Hai điều kiện này không hiển nhiên và làm mất thời gian nếu bỏ sót —
          đã gặp thật khi dựng: chứng chỉ tự ký và frame-ancestors. */}
      <Alert>
        <ShieldAlert className="size-4" aria-hidden="true" />
        <AlertTitle>Hai điều dễ bỏ sót</AlertTitle>
        <AlertDescription>
          <ul className="ml-4 list-disc space-y-1 text-sm">
            <li>
              MeshCentral phải cho phép hub nhúng nó:{' '}
              <code className="rounded bg-muted px-1 py-0.5">frame-ancestors</code> trong CSP phải
              có origin của hub, nếu không trình duyệt chặn khung.
            </li>
            <li>
              Nếu MeshCentral dùng chứng chỉ tự ký, mở nó ở tab riêng một lần để chấp nhận chứng chỉ
              — trình duyệt không hỏi bên trong iframe, chỉ hiện khung trắng.
            </li>
          </ul>
        </AlertDescription>
      </Alert>
    </div>
  )
}

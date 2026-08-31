import { ExternalLink, MonitorSmartphone, Settings2 } from 'lucide-react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useMeshCentralConfig } from '@/shared/api/meshCentral'

/**
 * Năng lực 6 — điều khiển thiết bị từ xa.
 *
 * §2.3: "tái sử dụng giao thức, đừng phát minh lại". Phần nặng giao cho
 * **MeshCentral** — công cụ mã nguồn mở đã có agent đóng gói sẵn cho
 * Windows/Linux/macOS, điều khiển nguồn, Wake-on-LAN, remote desktop, và giao
 * diện mobile riêng.
 *
 * Hub vẫn là control plane và UI: nó giữ đăng nhập (§6), bố cục, điều hướng, và
 * các năng lực khác. Tab này nhúng MeshCentral để dùng lại toàn bộ phần điều
 * khiển thay vì vẽ lại — kể cả trên điện thoại.
 */
export function RemotePage() {
  const { data, isPending, isError } = useMeshCentralConfig()

  return (
    <div className="flex h-full flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight lg:text-2xl">Điều khiển máy</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Tắt, khởi động lại, đánh thức, và điều khiển màn hình máy đã cài agent.
          </p>
        </div>

        {data?.configured && data.url ? (
          // Mở tab mới: một số thao tác (điều khiển màn hình toàn màn hình,
          // truyền file) chật chội trong iframe, nhất là trên điện thoại.
          <Button variant="outline" size="sm" asChild>
            <a href={data.url} target="_blank" rel="noopener noreferrer">
              <ExternalLink className="size-4" />
              Mở tab mới
            </a>
          </Button>
        ) : null}
      </div>

      {isPending ? <Skeleton className="h-[32rem] w-full rounded-lg" /> : null}

      {isError ? (
        <Alert variant="destructive">
          <AlertTitle>Không đọc được cấu hình</AlertTitle>
          <AlertDescription>
            Không hỏi được backend về địa chỉ MeshCentral. Kiểm tra backend còn chạy không.
          </AlertDescription>
        </Alert>
      ) : null}

      {data && !data.configured ? <SetupGuide /> : null}

      {data?.configured && data.url ? <MeshCentralFrame url={data.url} /> : null}
    </div>
  )
}

function MeshCentralFrame({ url }: { url: string }) {
  return (
    <iframe
      src={url}
      title="MeshCentral"
      // Chiều cao cố định thay vì h-full: iframe trong flex container co lại
      // thành 0 trên một số trình duyệt di động.
      className="min-h-[32rem] w-full flex-1 rounded-lg border bg-background"
      // Cho phép đủ quyền để remote desktop và truyền file chạy, nhưng vẫn
      // giữ sandbox — trang nhúng không đụng được vào hub.
      sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-downloads allow-modals"
      allow="clipboard-read; clipboard-write; fullscreen"
    />
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
    <Alert>
      <MonitorSmartphone className="size-4" />
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
          <Settings2 className="size-3.5 shrink-0" />
          Địa chỉ phải là địa chỉ tailnet mà điện thoại gọi tới được, không phải localhost.
        </p>
      </AlertDescription>
    </Alert>
  )
}

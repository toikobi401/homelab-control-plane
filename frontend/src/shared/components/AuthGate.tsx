import { AlertCircle } from 'lucide-react'
import type { ReactNode } from 'react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { LoginPage } from '@/features/auth/LoginPage'
import { SetupPage } from '@/features/auth/SetupPage'
import { useAuthGate } from '@/shared/api/useAuthGate'

/**
 * Quyết định người dùng thấy gì trước khi vào được ứng dụng.
 *
 * Đặt ngoài router: khi chưa đăng nhập thì không route nào hiện được, kể cả gõ
 * thẳng đường dẫn. Frontend không phải hàng rào bảo mật — backend vẫn trả 401
 * cho mọi endpoint cần xác thực (§6.5) — nhưng nó quyết định trải nghiệm.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const gate = useAuthGate()

  switch (gate.state) {
    case 'loading':
      return <GateLoading />

    case 'unreachable':
      return <GateUnreachable error={gate.error} />

    case 'needs-setup':
      return <SetupPage />

    case 'needs-login':
      return <LoginPage />

    case 'authenticated':
      return <>{children}</>
  }
}

function GateLoading() {
  return (
    <div className="flex min-h-dvh items-center justify-center px-4">
      <Card className="w-full max-w-sm" aria-busy="true">
        <CardHeader>
          <Skeleton className="h-5 w-32" />
          <Skeleton className="h-4 w-48" />
        </CardHeader>
        <CardContent className="space-y-3">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    </div>
  )
}

/**
 * Backend không trả lời. Khác hẳn "chưa đăng nhập": hiện màn hình đăng nhập lúc
 * này sẽ khiến người dùng gõ mật khẩu vào khoảng không rồi tưởng mình gõ sai.
 */
function GateUnreachable({ error }: { error: Error }) {
  return (
    <div className="flex min-h-dvh items-center justify-center px-4">
      <Alert variant="destructive" className="max-w-sm">
        <AlertCircle aria-hidden="true" />
        <AlertTitle>Không liên lạc được với hub</AlertTitle>
        <AlertDescription>
          <p>{error.message}</p>
          <p>Kiểm tra Tailscale trên thiết bị này còn kết nối không, và backend còn chạy không.</p>
        </AlertDescription>
      </Alert>
    </div>
  )
}

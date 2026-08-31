import { AlertCircle, LogIn } from 'lucide-react'
import { useState, type FormEvent } from 'react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useLogin } from '@/shared/api/auth'
import { ApiError } from '@/shared/api/client'

/**
 * Đăng nhập bằng mật khẩu (§6.3).
 *
 * Không có ô tên người dùng: hệ thống một người dùng, chỉ có một mật khẩu.
 */
export function LoginPage() {
  const [password, setPassword] = useState('')
  const login = useLogin()

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    login.mutate(password)
  }

  return (
    <AuthShell
      title="Đăng nhập"
      description="Nhập mật khẩu để mở khoá hub trên thiết bị này."
      error={login.error}
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="password">Mật khẩu</Label>
          <Input
            id="password"
            name="password"
            type="password"
            // Trình quản lý mật khẩu nhận diện được ô này nhờ autoComplete.
            autoComplete="current-password"
            autoFocus
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            disabled={login.isPending}
          />
        </div>

        <Button
          type="submit"
          className="w-full"
          disabled={login.isPending || password.length === 0}
        >
          <LogIn aria-hidden="true" />
          {login.isPending ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </Button>
      </form>
    </AuthShell>
  )
}

interface AuthShellProps {
  title: string
  description: string
  error: Error | null
  children: React.ReactNode
}

/**
 * Khung chung cho các màn hình xác thực. Căn giữa màn hình vì lúc này chưa có
 * điều hướng — không có gì khác để nhìn.
 */
export function AuthShell({ title, description, error, children }: AuthShellProps) {
  return (
    <div className="flex min-h-dvh items-center justify-center bg-background px-4 py-8">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {error ? <AuthError error={error} /> : null}
          {children}
        </CardContent>
      </Card>
    </div>
  )
}

export function AuthError({ error }: { error: Error }) {
  // 429 kèm số giây chờ trong `title` của ProblemDetails — backend khoá tăng dần
  // sau vài lần sai (§6.3). Người dùng cần thấy đúng thông báo đó, không phải
  // "mật khẩu không đúng" chung chung.
  const isLockout = error instanceof ApiError && error.status === 429

  return (
    <Alert variant="destructive">
      <AlertCircle aria-hidden="true" />
      <AlertTitle>{isLockout ? 'Tạm thời bị khoá' : 'Không đăng nhập được'}</AlertTitle>
      <AlertDescription>{error.message}</AlertDescription>
    </Alert>
  )
}

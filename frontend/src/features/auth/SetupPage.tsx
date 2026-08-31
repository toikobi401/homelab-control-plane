import { KeyRound } from 'lucide-react'
import { useState, type FormEvent } from 'react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useSetupPassword } from '@/shared/api/auth'

import { AuthShell } from './LoginPage'

/**
 * Độ dài tối thiểu, khớp `AuthOptions.MinimumPasswordLength` ở backend.
 *
 * Kiểm tra ở đây chỉ để báo sớm cho người dùng — backend vẫn kiểm lại và là nơi
 * quyết định. Frontend không phải hàng rào bảo mật (§6.5).
 */
const MIN_PASSWORD_LENGTH = 12

/**
 * Đặt mật khẩu lần đầu (§6.3).
 *
 * Backend chỉ chấp nhận request này từ localhost — mở trang từ điện thoại sẽ
 * nhận 403. Đó là chủ ý: không ai trong tailnet chiếm được hệ thống chưa khởi tạo.
 */
export function SetupPage() {
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const setup = useSetupPassword()

  const tooShort = password.length > 0 && password.length < MIN_PASSWORD_LENGTH
  const mismatch = confirmation.length > 0 && password !== confirmation
  const canSubmit =
    password.length >= MIN_PASSWORD_LENGTH && password === confirmation && !setup.isPending

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (canSubmit) {
      setup.mutate(password)
    }
  }

  return (
    <AuthShell
      title="Đặt mật khẩu"
      description="Lần đầu chạy hub. Đặt mật khẩu ngay trên máy chạy hệ thống — không đặt được từ thiết bị khác."
      error={setup.error}
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="new-password">Mật khẩu mới</Label>
          <Input
            id="new-password"
            name="new-password"
            type="password"
            autoComplete="new-password"
            autoFocus
            required
            minLength={MIN_PASSWORD_LENGTH}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            disabled={setup.isPending}
            aria-describedby="password-hint"
          />
          <p
            id="password-hint"
            className={tooShort ? 'text-xs text-destructive' : 'text-xs text-muted-foreground'}
          >
            Tối thiểu {MIN_PASSWORD_LENGTH} ký tự.
          </p>
        </div>

        <div className="space-y-2">
          <Label htmlFor="confirm-password">Nhập lại mật khẩu</Label>
          <Input
            id="confirm-password"
            name="confirm-password"
            type="password"
            autoComplete="new-password"
            required
            value={confirmation}
            onChange={(event) => setConfirmation(event.target.value)}
            disabled={setup.isPending}
            aria-invalid={mismatch}
            aria-describedby={mismatch ? 'confirm-error' : undefined}
          />
          {mismatch ? (
            <p id="confirm-error" className="text-xs text-destructive">
              Hai ô mật khẩu chưa khớp.
            </p>
          ) : null}
        </div>

        <Button type="submit" className="w-full" disabled={!canSubmit}>
          <KeyRound aria-hidden="true" />
          {setup.isPending ? 'Đang đặt mật khẩu…' : 'Đặt mật khẩu'}
        </Button>
      </form>
    </AuthShell>
  )
}

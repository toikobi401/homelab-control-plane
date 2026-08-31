import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { renderWithProviders } from '@/test/renderWithProviders'

import { AuthGate } from './AuthGate'

afterEach(() => {
  vi.unstubAllGlobals()
})

/**
 * Định tuyến fetch theo đường dẫn, để dựng đúng tổ hợp trạng thái backend.
 * `sessions` là undefined nghĩa là endpoint đó trả 401 (chưa đăng nhập).
 */
function stubBackend(options: {
  status?: { passwordConfigured: boolean }
  statusFails?: boolean
  sessions?: unknown[]
  sessionsStatus?: number
}) {
  vi.stubGlobal(
    'fetch',
    vi.fn<typeof fetch>().mockImplementation((input) => {
      // `input` có thể là string, URL, hay Request — chỉ URL thật mới có .url.
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url

      if (url.includes('/api/auth/status')) {
        if (options.statusFails) {
          return Promise.reject(new TypeError('Failed to fetch'))
        }
        return Promise.resolve(Response.json(options.status ?? { passwordConfigured: true }))
      }

      if (url.includes('/api/auth/sessions')) {
        if (options.sessions) {
          return Promise.resolve(Response.json(options.sessions))
        }
        return Promise.resolve(
          Response.json({ title: 'Chưa đăng nhập.' }, { status: options.sessionsStatus ?? 401 }),
        )
      }

      return Promise.resolve(Response.json({}))
    }),
  )
}

describe('AuthGate', () => {
  it('hiện màn hình đặt mật khẩu khi backend báo chưa có mật khẩu', async () => {
    stubBackend({ status: { passwordConfigured: false } })

    renderWithProviders(
      <AuthGate>
        <div>nội dung ứng dụng</div>
      </AuthGate>,
    )

    expect(await screen.findByLabelText('Mật khẩu mới')).toBeInTheDocument()
    expect(screen.getByLabelText('Nhập lại mật khẩu')).toBeInTheDocument()
    expect(screen.queryByText('nội dung ứng dụng')).not.toBeInTheDocument()
  })

  it('hiện màn hình đăng nhập khi đã có mật khẩu nhưng phiên trả 401', async () => {
    stubBackend({ status: { passwordConfigured: true } })

    renderWithProviders(
      <AuthGate>
        <div>nội dung ứng dụng</div>
      </AuthGate>,
    )

    expect(await screen.findByRole('button', { name: /Đăng nhập/ })).toBeInTheDocument()
    expect(screen.queryByText('nội dung ứng dụng')).not.toBeInTheDocument()
  })

  it('cho vào ứng dụng khi phiên hợp lệ', async () => {
    stubBackend({ status: { passwordConfigured: true }, sessions: [] })

    renderWithProviders(
      <AuthGate>
        <div>nội dung ứng dụng</div>
      </AuthGate>,
    )

    expect(await screen.findByText('nội dung ứng dụng')).toBeInTheDocument()
  })

  it('phân biệt backend chết với chưa đăng nhập — không hiện form đăng nhập', async () => {
    // Đây là chỗ dễ sai nhất: coi mọi lỗi là "chưa đăng nhập" sẽ khiến người dùng
    // gõ mật khẩu vào khoảng không mỗi khi mạng chập chờn.
    stubBackend({ statusFails: true })

    renderWithProviders(
      <AuthGate>
        <div>nội dung ứng dụng</div>
      </AuthGate>,
    )

    expect(await screen.findByText('Không liên lạc được với hub')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Đăng nhập/ })).not.toBeInTheDocument()
  })

  it('phiên chết giữa chừng thì quay về màn hình đăng nhập, không kẹt trong ứng dụng', async () => {
    // Kịch bản thật: đang dùng thì phiên bị thu hồi từ thiết bị khác (§6.3).
    let sessionAlive = true

    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockImplementation((input) => {
        const url =
          typeof input === 'string' ? input : input instanceof URL ? input.href : input.url

        if (url.includes('/api/auth/status')) {
          return Promise.resolve(Response.json({ passwordConfigured: true }))
        }

        if (sessionAlive) {
          return Promise.resolve(Response.json([]))
        }
        return Promise.resolve(Response.json({ title: 'Chưa đăng nhập.' }, { status: 401 }))
      }),
    )

    const { queryClient } = renderWithProviders(
      <AuthGate>
        <div>nội dung ứng dụng</div>
      </AuthGate>,
    )

    expect(await screen.findByText('nội dung ứng dụng')).toBeInTheDocument()

    sessionAlive = false
    await queryClient.invalidateQueries()

    expect(await screen.findByRole('button', { name: /Đăng nhập/ })).toBeInTheDocument()
    expect(screen.queryByText('nội dung ứng dụng')).not.toBeInTheDocument()
  })

  it('lỗi 500 ở /sessions là hỏng hóc, không phải chưa đăng nhập', async () => {
    stubBackend({ status: { passwordConfigured: true }, sessionsStatus: 500 })

    renderWithProviders(
      <AuthGate>
        <div>nội dung ứng dụng</div>
      </AuthGate>,
    )

    expect(await screen.findByText('Không liên lạc được với hub')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Đăng nhập/ })).not.toBeInTheDocument()
  })
})

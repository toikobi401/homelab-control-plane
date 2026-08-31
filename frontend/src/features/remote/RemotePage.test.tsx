import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { renderWithProviders } from '@/test/renderWithProviders'

import { RemotePage } from './RemotePage'

function mockConfig(body: unknown, status = 200) {
  const spy = vi.fn<typeof fetch>().mockResolvedValue(
    status === 200
      ? Response.json(body)
      : new Response(null, { status }),
  )
  vi.stubGlobal('fetch', spy)
  return spy
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('RemotePage', () => {
  it('nhúng MeshCentral khi đã cấu hình', async () => {
    mockConfig({ configured: true, url: 'https://hub.example.ts.net:4430' })

    renderWithProviders(<RemotePage />)

    const frame = await screen.findByTitle('MeshCentral')
    expect(frame).toHaveAttribute('src', 'https://hub.example.ts.net:4430')
  })

  /**
   * Chưa cấu hình mà hiện iframe trắng thì người vận hành không biết phải làm
   * gì — màn hình này là chỗ họ gặp đầu tiên.
   */
  it('hướng dẫn cài đặt khi chưa cấu hình, không hiện iframe trắng', async () => {
    mockConfig({ configured: false, url: null })

    renderWithProviders(<RemotePage />)

    expect(await screen.findByText(/Chưa cấu hình MeshCentral/i)).toBeInTheDocument()
    expect(screen.queryByTitle('MeshCentral')).not.toBeInTheDocument()
  })

  it('có nút mở tab mới — điều khiển màn hình chật chội trong iframe', async () => {
    mockConfig({ configured: true, url: 'https://hub.example.ts.net:4430' })

    renderWithProviders(<RemotePage />)

    const link = await screen.findByRole('link', { name: /Mở tab mới/i })
    expect(link).toHaveAttribute('href', 'https://hub.example.ts.net:4430')
    expect(link).toHaveAttribute('target', '_blank')

    // Thiếu noopener là để trang nhúng chạm được vào window.opener của hub.
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })

  it('báo lỗi rõ ràng khi không đọc được cấu hình', async () => {
    mockConfig(null, 500)

    renderWithProviders(<RemotePage />)

    expect(await screen.findByText(/Không đọc được cấu hình/i)).toBeInTheDocument()
  })
})

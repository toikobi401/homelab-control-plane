import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { renderWithProviders } from '@/test/renderWithProviders'

import { RemotePage } from './RemotePage'

function mockConfig(body: unknown, status = 200) {
  const spy = vi
    .fn<typeof fetch>()
    .mockResolvedValue(status === 200 ? Response.json(body) : new Response(null, { status }))
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

  it('hướng dẫn nêu cả hai cái bẫy: frame-ancestors và chứng chỉ tự ký', async () => {
    // Hai điều kiện này không hiển nhiên và tốn thời gian nếu bỏ sót — đã gặp
    // thật khi dựng.
    mockConfig({ configured: false, url: null })

    renderWithProviders(<RemotePage />)

    expect(await screen.findByText(/frame-ancestors/)).toBeInTheDocument()
    expect(screen.getByText(/chứng chỉ tự ký/i)).toBeInTheDocument()
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

  it('có lối thoát khi khung trắng — nguyên nhân hay gặp là chứng chỉ chưa tin', async () => {
    mockConfig({ configured: true, url: 'https://hub.example.ts.net:4430' })

    renderWithProviders(<RemotePage />)

    // Hiện thường trực: không có cách nào biết iframe hỏng hay không, và sự
    // kiện `load` không bắn khi trình duyệt chặn vì chứng chỉ — đoán rồi giấu
    // ghi chú đi thì đúng lúc cần nhất lại không có.
    expect(await screen.findByText(/Khung trống\?/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Xem cách khắc phục/i })).toBeInTheDocument()
  })

  it('mở hướng dẫn thì nói rõ nguyên nhân là chứng chỉ tự ký', async () => {
    mockConfig({ configured: true, url: 'https://hub.example.ts.net:4430' })

    const user = userEvent.setup()
    renderWithProviders(<RemotePage />)

    await user.click(await screen.findByRole('button', { name: /Xem cách khắc phục/i }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent(/chứng chỉ tự ký/i)
    // Hai lối đi, đúng thứ tự: mở tab chấp nhận chứng chỉ rồi mới thử lại.
    expect(within(dialog).getByRole('link', { name: /Mở MeshCentral ở tab mới/i })).toHaveAttribute(
      'href',
      'https://hub.example.ts.net:4430',
    )
    expect(within(dialog).getByRole('button', { name: /Thử lại/i })).toBeInTheDocument()
  })

  it('đóng hướng dẫn thì quay lại khung, không kẹt trong lớp phủ', async () => {
    mockConfig({ configured: true, url: 'https://hub.example.ts.net:4430' })

    const user = userEvent.setup()
    renderWithProviders(<RemotePage />)

    await user.click(await screen.findByRole('button', { name: /Xem cách khắc phục/i }))
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Đóng' }),
    )

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByTitle('MeshCentral')).toBeInTheDocument()
  })

  it('tải lại khung bằng cách remount iframe, không reload cả trang', async () => {
    mockConfig({ configured: true, url: 'https://hub.example.ts.net:4430' })

    const user = userEvent.setup()
    renderWithProviders(<RemotePage />)

    const before = await screen.findByTitle('MeshCentral')
    await user.click(screen.getByRole('button', { name: /Tải lại/i }))

    // key đổi → React tạo iframe mới, buộc trình duyệt tải lại nguồn.
    expect(screen.getByTitle('MeshCentral')).not.toBe(before)
  })

  it('báo lỗi rõ ràng khi không đọc được cấu hình', async () => {
    mockConfig(null, 500)

    renderWithProviders(<RemotePage />)

    expect(await screen.findByText(/Không đọc được cấu hình/i)).toBeInTheDocument()
  })
})

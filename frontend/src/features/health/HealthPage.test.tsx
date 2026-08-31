import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { renderWithProviders } from '@/test/renderWithProviders'

import { HealthPage } from './HealthPage'

afterEach(() => {
  vi.unstubAllGlobals()
})

/**
 * §7 yêu cầu xử lý đủ ba trạng thái. Test theo đúng ba trạng thái đó — bỏ sót
 * trạng thái lỗi là lỗi, nên nó phải có test riêng.
 */
describe('HealthPage', () => {
  it('hiện khung chờ trong lúc gọi backend', () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockReturnValue(new Promise<Response>(() => {})))

    const { container } = renderWithProviders(<HealthPage />)

    expect(container.querySelector('[aria-busy="true"]')).toBeInTheDocument()
    expect(screen.queryByText('Hub đang hoạt động')).not.toBeInTheDocument()
  })

  it('hiện trạng thái và thời điểm khi backend trả lời', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn<typeof fetch>()
        .mockResolvedValue(Response.json({ status: 'ok', utc: '2026-08-29T05:00:00Z' })),
    )

    renderWithProviders(<HealthPage />)

    expect(await screen.findByText('Hub đang hoạt động')).toBeInTheDocument()
    expect(screen.getByText('ok')).toBeInTheDocument()
  })

  it('hiện lỗi kèm gợi ý khi không gọi được backend', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch')),
    )

    renderWithProviders(<HealthPage />)

    expect(await screen.findByText('Không liên lạc được với hub')).toBeInTheDocument()
    expect(screen.getByText(/Tailscale/)).toBeInTheDocument()
  })

  it('không hiện thẻ trạng thái khi đang lỗi', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch')),
    )

    renderWithProviders(<HealthPage />)

    await screen.findByText('Không liên lạc được với hub')
    await waitFor(() => {
      expect(screen.queryByText('Hub đang hoạt động')).not.toBeInTheDocument()
    })
  })
})

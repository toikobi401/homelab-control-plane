import { screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { renderWithProviders } from '@/test/renderWithProviders'

import { DevicesPage } from './DevicesPage'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

function device(overrides: Record<string, unknown> = {}) {
  return {
    id: 'node-1',
    hostname: 'pc',
    name: 'pc.tailnet-example.ts.net',
    operatingSystem: 'windows',
    tailnetAddress: '100.100.100.100',
    lastSeen: new Date().toISOString(),
    isOnline: true,
    authorized: true,
    isExternal: false,
    clientVersion: '1.80.0',
    updateAvailable: false,
    ...overrides,
  }
}

function stubDevices(body: unknown, status = 200) {
  vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(Response.json(body, { status })))
}

describe('DevicesPage', () => {
  it('hiện khung chờ trong lúc gọi backend', () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockReturnValue(new Promise<Response>(() => {})))

    const { container } = renderWithProviders(<DevicesPage />)

    expect(container.querySelector('[aria-busy="true"]')).toBeInTheDocument()
  })

  it('liệt kê thiết bị kèm địa chỉ tailnet', async () => {
    stubDevices({ devices: [device()], onlineCount: 1 })

    renderWithProviders(<DevicesPage />)

    expect(await screen.findByText('pc')).toBeInTheDocument()
    expect(screen.getByText('pc.tailnet-example.ts.net')).toBeInTheDocument()
    expect(screen.getByText('100.100.100.100')).toBeInTheDocument()
  })

  it('nói "vừa hoạt động", không khẳng định máy đang bật', async () => {
    // isOnline do backend SUY RA từ lastSeen, Tailscale không trả trường này.
    // Trình bày phỏng đoán như sự thật là nói dối người dùng.
    stubDevices({ devices: [device()], onlineCount: 1 })

    renderWithProviders(<DevicesPage />)

    expect(await screen.findByText('Vừa hoạt động')).toBeInTheDocument()
    expect(screen.queryByText('Trực tuyến')).not.toBeInTheDocument()
  })

  it('hiện thời gian tương đối thay vì chỉ nói offline', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-29T12:00:00Z'))

    stubDevices({
      devices: [
        device({ id: 'a', hostname: 'laptop', lastSeen: '2026-08-29T11:58:00Z', isOnline: false }),
      ],
      onlineCount: 0,
    })

    renderWithProviders(<DevicesPage />)

    await vi.waitFor(() => {
      expect(screen.getByText('2 phút trước')).toBeInTheDocument()
    })
  })

  it('xử lý thiết bị chưa từng thấy — lastSeen null', async () => {
    stubDevices({
      devices: [device({ lastSeen: null, isOnline: false, tailnetAddress: null })],
      onlineCount: 0,
    })

    renderWithProviders(<DevicesPage />)

    expect(await screen.findByText('Chưa từng thấy')).toBeInTheDocument()
    expect(screen.getByText('Chỉ có IPv6')).toBeInTheDocument()
  })

  it('cảnh báo thiết bị chưa được duyệt', async () => {
    stubDevices({ devices: [device({ authorized: false })], onlineCount: 1 })

    renderWithProviders(<DevicesPage />)

    expect(await screen.findByText('Chưa được duyệt vào tailnet.')).toBeInTheDocument()
  })

  it('trạng thái rỗng khi tailnet chưa có thiết bị', async () => {
    stubDevices({ devices: [], onlineCount: 0 })

    renderWithProviders(<DevicesPage />)

    expect(await screen.findByText('Tailnet chưa có thiết bị nào.')).toBeInTheDocument()
  })

  it('503 là chưa cấu hình — hướng dẫn khắc phục, không phải báo hỏng', async () => {
    stubDevices({ title: 'Chưa cấu hình Tailscale.' }, 503)

    renderWithProviders(<DevicesPage />)

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText('Chưa cấu hình Tailscale')).toBeInTheDocument()
    expect(within(alert).getByText(/OAuth client ID/)).toBeInTheDocument()
  })

  it('502 là Tailscale trục trặc — khác hẳn chưa cấu hình', async () => {
    stubDevices({ title: 'Không gọi được Tailscale.' }, 502)

    renderWithProviders(<DevicesPage />)

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText('Không gọi được Tailscale')).toBeInTheDocument()
    expect(within(alert).getByText(/thử lại sau ít phút/)).toBeInTheDocument()
  })
})

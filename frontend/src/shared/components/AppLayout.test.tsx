import { screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { renderWithProviders } from '@/test/renderWithProviders'

import { AppLayout } from './AppLayout'
import { mobileNavItems, navItems } from './navItems'

afterEach(() => {
  vi.unstubAllGlobals()
})

function renderLayout(route = '/') {
  // AppLayout render HealthIndicator, thứ gọi /health ngay khi mount.
  vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(Response.json({ status: 'ok' })))
  return renderWithProviders(<AppLayout />, { route })
}

describe('AppLayout', () => {
  it('có hai thanh điều hướng: một cho điện thoại, một cho desktop', () => {
    renderLayout()

    // Cả hai cùng nằm trong DOM, CSS quyết định cái nào hiện. Đây là chủ ý:
    // đổi khổ màn hình không phải remount, không mất trạng thái.
    expect(screen.getAllByRole('navigation', { name: 'Điều hướng chính' })).toHaveLength(2)
  })

  it('sidebar desktop có đủ mọi mục, kể cả mục chỉ dành cho desktop', () => {
    renderLayout()

    const sidebar = screen.getByLabelText('Điều hướng chính', { selector: 'aside nav' })

    for (const item of navItems) {
      expect(within(sidebar).getByRole('link', { name: item.label })).toBeInTheDocument()
    }
  })

  it('thanh dưới cùng của điện thoại bỏ mục desktopOnly — màn hình hẹp không đủ chỗ', () => {
    renderLayout()

    const desktopOnly = navItems.filter((item) => item.desktopOnly)
    expect(desktopOnly.length).toBeGreaterThan(0)

    const bottomNav = screen.getByLabelText('Điều hướng chính', { selector: 'nav.sticky.bottom-0' })

    // Mục desktopOnly không có trong thanh dưới cùng.
    for (const item of desktopOnly) {
      expect(within(bottomNav).queryByRole('link', { name: item.label })).not.toBeInTheDocument()
    }

    // Mục thường thì có.
    for (const item of mobileNavItems) {
      expect(within(bottomNav).getByRole('link', { name: item.label })).toBeInTheDocument()
    }
  })

  it('thanh tiêu đề điện thoại có lối vào trang Phiên', () => {
    // Trang Phiên là desktopOnly nên vắng ở thanh dưới cùng. Không có lối vào
    // riêng thì trên điện thoại không "đăng xuất tất cả thiết bị" được.
    renderLayout()

    expect(screen.getByRole('link', { name: 'Phiên đăng nhập' })).toHaveAttribute(
      'href',
      '/sessions',
    )
  })

  it('đánh dấu đúng mục đang mở', () => {
    renderLayout('/devices')

    for (const link of screen.getAllByRole('link', { name: 'Thiết bị' })) {
      expect(link).toHaveAttribute('aria-current', 'page')
    }
  })

  it('mục Trạng thái chỉ active ở đúng "/", không active ở trang khác', () => {
    // NavLink không có `end` sẽ khớp "/" với mọi đường dẫn và luôn sáng.
    renderLayout('/devices')

    expect(screen.getByRole('link', { name: 'Trạng thái' })).not.toHaveAttribute('aria-current')
  })
})

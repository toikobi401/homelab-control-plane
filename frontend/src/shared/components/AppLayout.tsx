import { ShieldCheck } from 'lucide-react'
import { NavLink, Outlet } from 'react-router-dom'

import { cn } from '@/shared/lib/utils'

import { HealthIndicator } from './HealthIndicator'
import { mobileNavItems, navItems } from './navItems'

/**
 * Khung giao diện chung cho mọi màn hình.
 *
 * Một layout, hai hình dạng — không phải hai codebase. CONTEXT.md §3 là
 * mobile-first: mặc định (không tiền tố) là điện thoại, biến thể `lg:` là desktop.
 * Cùng route, cùng component trang; chỉ khung điều hướng đổi chỗ.
 *
 * Mốc `lg` (1024px) chọn theo chỗ thật sự đủ cho sidebar 15rem cộng nội dung
 * rộng thoải mái, không phải theo tên thiết bị. Tablet ngang rơi vào desktop,
 * tablet dọc vẫn dùng thanh dưới cùng — đúng với cách cầm máy.
 */
export function AppLayout() {
  return (
    <div className="min-h-dvh bg-background lg:flex">
      <DesktopSidebar />

      <div className="flex min-h-dvh flex-1 flex-col lg:min-h-dvh lg:overflow-x-hidden">
        <MobileHeader />
        <DesktopHeader />

        {/* Điện thoại: căn giữa (mx-auto) vì màn hình hẹp, lề hai bên bằng nhau
            trông cân. Desktop: căn trái (lg:mx-0) để nội dung bám sidebar —
            căn giữa trên màn hình rất rộng đẩy nội dung ra xa sidebar, mắt phải
            quét qua một khoảng trống lớn mỗi lần chuyển trang. */}
        <main className="mx-auto w-full max-w-3xl flex-1 px-4 py-5 lg:mx-0 lg:max-w-5xl lg:px-8 lg:py-8">
          <Outlet />
        </main>

        <MobileNav />
      </div>
    </div>
  )
}

/** Thanh tiêu đề của điện thoại. Desktop có tiêu đề riêng, gọn hơn. */
function MobileHeader() {
  return (
    <header className="sticky top-0 z-10 border-b bg-background/95 pt-[var(--safe-top)] backdrop-blur lg:hidden">
      <div className="mx-auto flex h-14 w-full max-w-3xl items-center justify-between gap-3 px-4">
        <span className="text-base font-semibold tracking-tight">Device Hub</span>
        <div className="flex items-center gap-1">
          <HealthIndicator />
          {/* Trang Phiên là desktopOnly nên vắng mặt ở thanh dưới cùng. Không có
              lối vào này thì trên điện thoại không thể "đăng xuất tất cả thiết
              bị" — đúng lúc cần nhất là khi mất một thiết bị khác. */}
          <NavLink
            to="/sessions"
            className="flex size-9 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground"
            aria-label="Phiên đăng nhập"
          >
            <ShieldCheck className="size-5" aria-hidden="true" />
          </NavLink>
        </div>
      </div>
    </header>
  )
}

/**
 * Thanh tiêu đề desktop: chỉ còn chấm trạng thái, vì tên ứng dụng đã nằm ở
 * đầu sidebar — lặp lại là thừa.
 */
function DesktopHeader() {
  return (
    // Header trải hết bề rộng, không bó theo max-w của nội dung: trên màn hình
    // rất rộng, chấm trạng thái bó theo nội dung sẽ đứng lơ lửng giữa màn hình
    // thay vì ở mép phải, trông như bố cục hỏng.
    <header className="sticky top-0 z-10 hidden border-b bg-background/95 backdrop-blur lg:block">
      <div className="flex h-14 items-center justify-end px-8">
        <HealthIndicator />
      </div>
    </header>
  )
}

/**
 * Sidebar desktop. Chuột trỏ chính xác nên mục điều hướng nằm ngang, chữ đầy đủ,
 * vùng chạm nhỏ hơn thanh dưới cùng của điện thoại — ngược lại sẽ thừa chỗ.
 */
function DesktopSidebar() {
  return (
    <aside className="hidden w-60 shrink-0 border-r bg-card lg:flex lg:h-dvh lg:sticky lg:top-0 lg:flex-col">
      <div className="flex h-14 items-center border-b px-5">
        <span className="text-base font-semibold tracking-tight">Device Hub</span>
      </div>

      <nav className="flex-1 overflow-y-auto p-3" aria-label="Điều hướng chính">
        <ul className="space-y-1">
          {navItems.map(({ to, label, icon: Icon }) => (
            <li key={to}>
              <NavLink
                to={to}
                // `end` chỉ cho mục gốc: không có nó, "/" khớp với mọi đường dẫn
                // và luôn hiện như đang được chọn.
                end={to === '/'}
                className={({ isActive }) =>
                  cn(
                    'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
                    isActive
                      ? 'bg-accent text-accent-foreground'
                      : 'text-muted-foreground hover:bg-accent/50 hover:text-foreground',
                  )
                }
              >
                {({ isActive }) => (
                  <>
                    <Icon
                      className="size-4 shrink-0"
                      strokeWidth={isActive ? 2.4 : 1.8}
                      aria-hidden="true"
                    />
                    <span className="truncate">{label}</span>
                  </>
                )}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
    </aside>
  )
}

/**
 * Thanh điều hướng dưới cùng của điện thoại: ngón cái với tới được, khác với
 * thanh trên cùng vốn là quy ước của desktop. pb né thanh home của iPhone.
 */
function MobileNav() {
  return (
    <nav
      className="sticky bottom-0 z-10 border-t bg-background/95 pb-[var(--safe-bottom)] backdrop-blur lg:hidden"
      aria-label="Điều hướng chính"
    >
      <ul className="mx-auto flex w-full max-w-3xl items-stretch">
        {mobileNavItems.map(({ to, label, icon: Icon }) => (
          <li key={to} className="flex-1">
            <NavLink
              to={to}
              className={({ isActive }) =>
                cn(
                  // min-h-14: vùng chạm tối thiểu cho ngón tay (§3).
                  'flex min-h-14 flex-col items-center justify-center gap-1 px-1 text-[0.6875rem] font-medium transition-colors',
                  isActive
                    ? 'text-foreground'
                    : 'text-muted-foreground hover:text-foreground active:text-foreground',
                )
              }
            >
              {({ isActive }) => (
                <>
                  <Icon
                    className="size-5 shrink-0"
                    strokeWidth={isActive ? 2.4 : 1.8}
                    aria-hidden="true"
                  />
                  <span className="truncate">{label}</span>
                </>
              )}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}

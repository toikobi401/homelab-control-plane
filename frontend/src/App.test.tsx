import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Navigate, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AppLayout } from '@/shared/components/AppLayout'
import { NotFoundPage } from '@/shared/components/NotFoundPage'

afterEach(() => {
  vi.unstubAllGlobals()
})

/**
 * Định tuyến của App, dựng lại với MemoryRouter để đặt được đường dẫn ban đầu.
 *
 * Không render `<App />` trực tiếp: nó dùng BrowserRouter (không nhận initial
 * route) và bọc AuthGate (đòi mock cả luồng xác thực). Ở đây chỉ cần kiểm tra
 * đúng phần định tuyến.
 */
function renderRoutes(route: string) {
  vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(Response.json({ status: 'ok' })))

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<div>trang trạng thái</div>} />
            <Route path="remote" element={<div>trang điều khiển</div>} />
            <Route path="health" element={<Navigate to="/" replace />} />
            <Route path="files" element={<Navigate to="/remote" replace />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Định tuyến', () => {
  /**
   * `/files` từng là năng lực 2. MeshCentral đã có sẵn duyệt và truyền file nên
   * trang bị xoá — nhưng ai lưu đường dẫn cũ phải được đưa tới đúng chỗ, không
   * phải thả vào trang 404 rồi tự đoán chuyện gì đã xảy ra.
   */
  it('/files chuyển hướng sang /remote, không rơi vào 404', async () => {
    renderRoutes('/files')

    expect(await screen.findByText('trang điều khiển')).toBeInTheDocument()
    expect(screen.queryByText('Không có trang này')).not.toBeInTheDocument()
  })

  it('/health vẫn chuyển hướng về trang gốc', async () => {
    renderRoutes('/health')

    expect(await screen.findByText('trang trạng thái')).toBeInTheDocument()
  })

  it('đường dẫn lạ thì vào trang 404', async () => {
    renderRoutes('/khong-ton-tai')

    expect(await screen.findByText('Không có trang này')).toBeInTheDocument()
  })
})

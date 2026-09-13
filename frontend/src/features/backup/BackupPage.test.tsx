import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { __resetCsrfTokenForTests } from '@/shared/api/client'
import { renderWithProviders } from '@/test/renderWithProviders'

import { BackupPage } from './BackupPage'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
  __resetCsrfTokenForTests()
})

function run(overrides: Record<string, unknown> = {}) {
  return {
    id: 1,
    jobName: 'tai-lieu',
    status: 'Succeeded',
    startedAt: '2026-01-01T02:00:00Z',
    finishedAt: '2026-01-01T02:01:12Z',
    filesTransferred: 1204,
    bytesTransferred: 3_435_973_836,
    errors: 0,
    errorMessage: null,
    ...overrides,
  }
}

function job(overrides: Record<string, unknown> = {}) {
  return {
    name: 'tai-lieu',
    encrypted: false,
    deleteExtra: false,
    isRunning: false,
    isEditable: true,
    latestRun: run(),
    ...overrides,
  }
}

/** `fetch` nhận string | URL | Request — lấy URL ra mà không ép kiểu bừa. */
function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  if (input instanceof URL) return input.href
  return input.url
}

/**
 * Trang gọi hai endpoint (trạng thái và lịch sử) và có thể POST để chạy job.
 * Định tuyến theo URL thay vì theo thứ tự gọi — thứ tự phụ thuộc vào lúc nào
 * React Query quyết định fetch, một chi tiết không nên làm test vỡ.
 */
function stubApi({
  status,
  history = [],
  runResponse,
}: {
  status: unknown
  history?: unknown
  runResponse?: { body: unknown; status: number }
}) {
  const fetchMock = vi.fn<typeof fetch>((input, init) => {
    const url = requestUrl(input)
    const method = init?.method ?? 'GET'

    if (url.includes('/api/antiforgery/token')) {
      return Promise.resolve(Response.json({ token: 'test-token', headerName: 'X-CSRF-Token' }))
    }
    if (method === 'POST' && url.includes('/run')) {
      const reply = runResponse ?? { body: run(), status: 200 }
      return Promise.resolve(Response.json(reply.body, { status: reply.status }))
    }
    if (url.includes('/api/backup/history')) {
      return Promise.resolve(Response.json(history))
    }
    return Promise.resolve(Response.json(status))
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('BackupPage', () => {
  it('hiện khung chờ trong lúc gọi backend', () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockReturnValue(new Promise<Response>(() => {})))

    const { container } = renderWithProviders(<BackupPage />)

    expect(container.querySelector('[aria-busy="true"]')).toBeInTheDocument()
  })

  it('liệt kê công việc kèm số liệu lần chạy gần nhất', async () => {
    stubApi({ status: { rcloneAvailable: true, rcloneVersion: 'rclone v1.68.2', jobs: [job()] } })

    renderWithProviders(<BackupPage />)

    expect(await screen.findByText('tai-lieu')).toBeInTheDocument()
    // Số lượng và dung lượng — không có tên tệp.
    expect(screen.getByText(/1\.204 tệp/)).toBeInTheDocument()
    expect(screen.getByText(/3,2 GiB/)).toBeInTheDocument()
  })

  /**
   * Job khai tay trong appsettings không xoá được qua API — hiện nút xoá cho nó
   * là bẫy người dùng: bấm vào chắc chắn nhận 404. Đã gặp thật.
   */
  it('ẩn nút xoá với công việc khai trong file cấu hình', async () => {
    stubApi({
      status: {
        rcloneAvailable: true,
        rcloneVersion: 'rclone v1.75.1',
        jobs: [job({ name: 'hub-data', isEditable: false })],
      },
    })

    renderWithProviders(<BackupPage />)
    await screen.findByText('hub-data')

    expect(screen.queryByLabelText('Xoá hub-data')).not.toBeInTheDocument()
    expect(screen.getByText('khai trong cấu hình')).toBeInTheDocument()
  })

  it('hiện nút xoá với công việc tạo từ giao diện', async () => {
    stubApi({
      status: {
        rcloneAvailable: true,
        rcloneVersion: 'rclone v1.75.1',
        jobs: [job({ name: 'tai-lieu', isEditable: true })],
      },
    })

    renderWithProviders(<BackupPage />)
    await screen.findByText('tai-lieu')

    expect(screen.getByLabelText('Xoá tai-lieu')).toBeInTheDocument()
  })

  it('KHÔNG hiện đường dẫn tệp — DTO cố tình không trả Source/Destination', async () => {
    stubApi({
      status: { rcloneAvailable: true, rcloneVersion: 'rclone v1.68.2', jobs: [job()] },
      history: [run()],
    })

    const { container } = renderWithProviders(<BackupPage />)
    await screen.findByText('tai-lieu')

    // §6.5 mục 4: không đường dẫn nào được lọt ra giao diện.
    expect(container.textContent).not.toMatch(/[A-Z]:\\/)
    expect(container.textContent).not.toMatch(/drive:|crypt:/)
  })

  it('nói rõ chế độ sync xoá tệp thừa ở đích', async () => {
    stubApi({
      status: {
        rcloneAvailable: true,
        rcloneVersion: 'rclone v1.68.2',
        jobs: [job({ deleteExtra: true })],
      },
    })

    renderWithProviders(<BackupPage />)

    expect(await screen.findByText(/xoá tệp thừa ở đích \(sync\)/)).toBeInTheDocument()
  })

  it('kết luận nói thật khi còn công việc chưa xong', async () => {
    stubApi({
      status: {
        rcloneAvailable: true,
        rcloneVersion: 'rclone v1.68.2',
        jobs: [
          job(),
          job({ name: 'anh', latestRun: run({ id: 2, jobName: 'anh', status: 'Failed' }) }),
        ],
      },
    })

    renderWithProviders(<BackupPage />)

    expect(await screen.findByText(/1 trong 2 công việc chưa xong/)).toBeInTheDocument()
  })

  it('gọi "Cancelled" là máy tắt giữa chừng, không đổ lỗi cho người dùng', async () => {
    stubApi({
      status: {
        rcloneAvailable: true,
        rcloneVersion: 'rclone v1.68.2',
        jobs: [
          job({
            latestRun: run({
              status: 'Cancelled',
              errorMessage: 'Quá 120 phút, đã dừng.',
              finishedAt: null,
            }),
          }),
        ],
      },
    })

    renderWithProviders(<BackupPage />)

    expect(await screen.findByText(/Chạy lại để tiếp tục/)).toBeInTheDocument()
    expect(screen.queryByText(/Đã huỷ/)).not.toBeInTheDocument()
  })

  it('cảnh báo khi thiếu rclone — lỗi cấu hình, phải nói trước khi bấm chạy', async () => {
    stubApi({
      status: { rcloneAvailable: false, rcloneVersion: null, jobs: [job()] },
    })

    renderWithProviders(<BackupPage />)

    expect(await screen.findByText('Không gọi được rclone')).toBeInTheDocument()
  })

  it('mời khai công việc khi chưa cấu hình job nào', async () => {
    stubApi({
      status: { rcloneAvailable: true, rcloneVersion: 'rclone v1.68.2', jobs: [] },
    })

    renderWithProviders(<BackupPage />)

    expect(await screen.findByText('Chưa có công việc sao lưu nào.')).toBeInTheDocument()
    expect(screen.getByText(/rclone v1\.68\.2 · sẵn sàng/)).toBeInTheDocument()
  })

  it('khoá nút khi job đang chạy — bấm hai lần không nên thành hai lần sao lưu', async () => {
    stubApi({
      status: {
        rcloneAvailable: true,
        rcloneVersion: 'rclone v1.68.2',
        jobs: [job({ isRunning: true })],
      },
    })

    renderWithProviders(<BackupPage />)

    const button = await screen.findByRole('button', { name: 'Đang chạy' })
    expect(button).toBeDisabled()
  })

  it('gửi POST kèm CSRF token khi bấm Chạy ngay', async () => {
    const fetchMock = stubApi({
      status: { rcloneAvailable: true, rcloneVersion: 'rclone v1.68.2', jobs: [job()] },
    })

    renderWithProviders(<BackupPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Chạy ngay' }))

    await waitFor(() => {
      const posted = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')
      expect(posted).toBeDefined()
      expect(requestUrl(posted![0])).toContain('/api/backup/tai-lieu/run')
      expect((posted![1]?.headers as Record<string, string>)['X-CSRF-Token']).toBe('test-token')
    })
  })

  it('hiện lời từ chối của backend khi job đang chạy (409)', async () => {
    stubApi({
      status: { rcloneAvailable: true, rcloneVersion: 'rclone v1.68.2', jobs: [job()] },
      runResponse: { body: { title: 'Công việc này đang chạy.' }, status: 409 },
    })

    renderWithProviders(<BackupPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Chạy ngay' }))

    expect(await screen.findByText('Công việc này đang chạy.')).toBeInTheDocument()
  })

  it('lịch sử nói rõ là không lưu tên tệp', async () => {
    stubApi({
      status: { rcloneAvailable: true, rcloneVersion: 'rclone v1.68.2', jobs: [job()] },
      history: [run(), run({ id: 2, jobName: 'anh', status: 'Failed' })],
    })

    renderWithProviders(<BackupPage />)

    expect(
      await screen.findByText(/Chỉ lưu số lượng và dung lượng — không lưu tên tệp/),
    ).toBeInTheDocument()

    // Lần chạy hỏng không có dung lượng để khoe — cột đó phải là gạch ngang,
    // không phải "0 B" (nhầm giữa "không truyền được" và "truyền 0 byte").
    const history = screen.getByRole('heading', { name: 'Lịch sử' }).closest('section')
    expect(history).not.toBeNull()
    expect(within(history!).getByText('anh')).toBeInTheDocument()
    expect(within(history!).getAllByText('—').length).toBeGreaterThan(0)
  })

  it('báo lỗi khi không đọc được trạng thái', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(Response.json({ title: 'Hỏng' }, { status: 500 })),
    )

    renderWithProviders(<BackupPage />)

    expect(await screen.findByText('Không đọc được trạng thái sao lưu')).toBeInTheDocument()
  })
})

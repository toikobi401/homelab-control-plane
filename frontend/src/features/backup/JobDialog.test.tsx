import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { __resetCsrfTokenForTests } from '@/shared/api/client'
import { renderWithProviders } from '@/test/renderWithProviders'

import { JobDialog } from './JobDialog'

afterEach(() => {
  vi.unstubAllGlobals()
  __resetCsrfTokenForTests()
})

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  if (input instanceof URL) return input.href
  return input.url
}

const roots = {
  path: null,
  parent: null,
  entries: [{ name: 'D:\\Du lieu', path: 'D:\\Du lieu' }],
}

const presets = [
  {
    name: 'Dự án code',
    description: 'Bỏ thư mục build.',
    content: '- node_modules/**\n+ **\n',
  },
]

function stubApi({ saveResponse }: { saveResponse?: { body: unknown; status: number } } = {}) {
  const fetchMock = vi.fn<typeof fetch>((input, init) => {
    const url = requestUrl(input)
    const method = init?.method ?? 'GET'

    if (url.includes('/api/antiforgery/token')) {
      return Promise.resolve(Response.json({ token: 'test-token', headerName: 'X-CSRF-Token' }))
    }
    if (url.includes('/api/backup/presets')) {
      return Promise.resolve(Response.json(presets))
    }
    if (url.includes('/api/backup/browse')) {
      return Promise.resolve(Response.json(roots))
    }
    if (method === 'POST' && url.includes('/api/backup/jobs')) {
      const reply = saveResponse ?? {
        body: { name: 'tai-lieu', filterFile: null },
        status: 200,
      }
      return Promise.resolve(Response.json(reply.body, { status: reply.status }))
    }
    return Promise.resolve(Response.json({}))
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** Lấy body của request POST tạo job, đã parse. */
function savedBody(fetchMock: ReturnType<typeof stubApi>): unknown {
  const call = fetchMock.mock.calls.find(
    ([input, init]) =>
      (init?.method ?? 'GET') === 'POST' && requestUrl(input).includes('/api/backup/jobs'),
  )
  const raw = call?.[1]?.body
  return typeof raw === 'string' ? JSON.parse(raw) : raw
}

async function fillRequiredFields() {
  await userEvent.type(screen.getByLabelText('Tên'), 'tai-lieu')
  await userEvent.click(await screen.findByText('D:\\Du lieu'))
  await userEvent.type(screen.getByLabelText('Đích trên cloud'), 'hub:backup/tai-lieu')
}

describe('JobDialog', () => {
  it('không cho lưu khi còn thiếu trường bắt buộc', async () => {
    stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Tạo công việc' })).toBeDisabled()

    // Có tên nhưng chưa chọn thư mục thì vẫn chưa đủ.
    await userEvent.type(screen.getByLabelText('Tên'), 'tai-lieu')
    expect(screen.getByRole('button', { name: 'Tạo công việc' })).toBeDisabled()
  })

  /**
   * Ô nhập đường dẫn là cách nhanh nhất khi đã biết đường dẫn — dán từ File
   * Explorer là xong. Phải gửi lên đúng chuỗi đã gõ, không qua cây thư mục.
   */
  it('gõ thẳng đường dẫn vào ô nhập cũng lưu được', async () => {
    const fetchMock = stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)

    await userEvent.type(screen.getByLabelText('Tên'), 'anh-cu')
    await userEvent.type(screen.getByLabelText('Thư mục nguồn'), 'E:\\Anh\\2026')
    await userEvent.type(screen.getByLabelText('Đích trên cloud'), 'hub:backup/anh-cu')
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

    await waitFor(() => expect(savedBody(fetchMock)).toBeTruthy())

    expect(savedBody(fetchMock)).toMatchObject({
      name: 'anh-cu',
      source: 'E:\\Anh\\2026',
    })
  })

  it('chọn trong cây thì điền vào ô nhập', async () => {
    stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
    await userEvent.click(await screen.findByText('D:\\Du lieu'))

    expect(screen.getByLabelText('Thư mục nguồn')).toHaveValue('D:\\Du lieu')
  })

  it('gửi đúng dữ liệu đã nhập', async () => {
    const fetchMock = stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

    await waitFor(() => expect(savedBody(fetchMock)).toBeTruthy())

    expect(savedBody(fetchMock)).toMatchObject({
      name: 'tai-lieu',
      source: 'D:\\Du lieu',
      destination: 'hub:backup/tai-lieu',
      encrypted: false,
      deleteExtra: false,
      filterContent: null,
    })
  })

  /**
   * `apiFetch` tự JSON.stringify. Stringify thêm một lần nữa ở hook sẽ gửi một
   * chuỗi JSON lồng trong JSON, và backend trả 400 với body rỗng — kiểu lỗi rất
   * khó lần ra vì trông như request hợp lệ.
   */
  it('body là object JSON một lớp, không bị mã hoá hai lần', async () => {
    const fetchMock = stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

    await waitFor(() => expect(savedBody(fetchMock)).toBeTruthy())

    // Mã hoá hai lần thì parse ra chuỗi, không phải object.
    expect(typeof savedBody(fetchMock)).toBe('object')
  })

  it('bấm mẫu thì điền nội dung lọc', async () => {
    const fetchMock = stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
    await userEvent.click(await screen.findByRole('button', { name: 'Dự án code' }))

    expect(screen.getByLabelText(/Bỏ qua tệp/)).toHaveValue('- node_modules/**\n+ **\n')

    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))
    await waitFor(() => expect(savedBody(fetchMock)).toBeTruthy())

    expect(savedBody(fetchMock)).toMatchObject({ filterContent: '- node_modules/**\n+ **\n' })
  })

  /**
   * `apiFetch` lấy `title` trước rồi mới tới `detail` (client.ts). Backend trả
   * cả hai, nên thứ người dùng thấy khi trùng tên là chữ "Conflict" — không
   * phải câu tiếng Việt trong `detail`.
   *
   * Test khẳng định đúng hành vi THẬT, không phải hành vi mong muốn. Đổi thứ tự
   * ưu tiên trong client là việc chung của mọi tính năng, không gộp vào đây.
   */
  it('hiện lỗi khi backend từ chối, không đóng hộp thoại', async () => {
    stubApi({
      saveResponse: {
        body: { title: 'Conflict', detail: 'Đã có công việc cùng tên khai trong file cấu hình.' },
        status: 409,
      },
    })
    const onOpenChange = vi.fn()

    renderWithProviders(<JobDialog open onOpenChange={onOpenChange} />)
    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

    expect(await screen.findByText('Conflict')).toBeInTheDocument()
    expect(onOpenChange).not.toHaveBeenCalledWith(false)
  })

  it('dùng detail khi backend không trả title', async () => {
    stubApi({
      saveResponse: {
        body: { detail: 'Thư mục nguồn không nằm trong phạm vi được phép.' },
        status: 400,
      },
    })

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

    expect(await screen.findByText(/không nằm trong phạm vi/)).toBeInTheDocument()
  })

  it('đóng hộp thoại sau khi lưu xong', async () => {
    stubApi()
    const onOpenChange = vi.fn()

    renderWithProviders(<JobDialog open onOpenChange={onOpenChange} />)
    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
  })
})

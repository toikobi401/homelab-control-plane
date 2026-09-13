import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { __resetCsrfTokenForTests } from '@/shared/api/client'
import { renderWithProviders } from '@/test/renderWithProviders'

import { FolderPicker } from './FolderPicker'

afterEach(() => {
  vi.unstubAllGlobals()
  __resetCsrfTokenForTests()
})

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  if (input instanceof URL) return input.href
  return input.url
}

/**
 * Backend trả danh sách gốc khi không có `path`, và thư mục con khi có.
 * Định tuyến theo tham số `path` thay vì theo thứ tự gọi — thứ tự phụ thuộc
 * lúc nào React Query quyết định fetch.
 */
function stubBrowse(byPath: Record<string, unknown>) {
  const fetchMock = vi.fn<typeof fetch>((input) => {
    const url = new URL(requestUrl(input), 'https://hub.test')
    const path = url.searchParams.get('path') ?? ''

    const body = byPath[path]
    if (body === undefined) {
      return Promise.resolve(Response.json({ title: 'Không tìm thấy' }, { status: 400 }))
    }
    return Promise.resolve(Response.json(body))
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const roots = {
  path: null,
  parent: null,
  entries: [
    { name: 'D:\\Du lieu', path: 'D:\\Du lieu' },
    { name: 'C:\\Users\\x\\Documents', path: 'C:\\Users\\x\\Documents' },
  ],
}

const inside = {
  path: 'D:\\Du lieu',
  parent: null,
  entries: [
    { name: 'anh', path: 'D:\\Du lieu\\anh' },
    { name: 'tai-lieu', path: 'D:\\Du lieu\\tai-lieu' },
  ],
}

describe('FolderPicker', () => {
  it('hiện danh sách gốc khi chưa chọn gì', async () => {
    stubBrowse({ '': roots })

    renderWithProviders(<FolderPicker value={null} onChange={vi.fn()} />)

    expect(await screen.findByText('D:\\Du lieu')).toBeInTheDocument()
    expect(screen.getByText('C:\\Users\\x\\Documents')).toBeInTheDocument()
  })

  /**
   * Bấm tên là CHỌN, bấm mũi tên là MỞ. Gộp hai hành động làm một thì không
   * chọn được thư mục có thư mục con — đúng thứ người dùng cần nhất.
   */
  it('bấm tên thì chọn, không đi vào trong', async () => {
    stubBrowse({ '': roots, 'D:\\Du lieu': inside })
    const onChange = vi.fn()

    renderWithProviders(<FolderPicker value={null} onChange={onChange} />)
    await userEvent.click(await screen.findByText('D:\\Du lieu'))

    expect(onChange).toHaveBeenCalledWith('D:\\Du lieu')
    // Vẫn ở danh sách gốc: không có thư mục con nào hiện ra.
    expect(screen.queryByText('tai-lieu')).not.toBeInTheDocument()
  })

  it('bấm mũi tên thì đi vào trong, không chọn', async () => {
    stubBrowse({ '': roots, 'D:\\Du lieu': inside })
    const onChange = vi.fn()

    renderWithProviders(<FolderPicker value={null} onChange={onChange} />)
    await userEvent.click(await screen.findByLabelText('Mở D:\\Du lieu'))

    expect(await screen.findByText('tai-lieu')).toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('quay lên trên được sau khi đi vào trong', async () => {
    stubBrowse({ '': roots, 'D:\\Du lieu': inside })

    renderWithProviders(<FolderPicker value={null} onChange={vi.fn()} />)
    await userEvent.click(await screen.findByLabelText('Mở D:\\Du lieu'))
    await screen.findByText('tai-lieu')

    await userEvent.click(screen.getByRole('button', { name: /Lên trên/ }))

    expect(await screen.findByText('C:\\Users\\x\\Documents')).toBeInTheDocument()
  })

  /**
   * Backend chặn đường dẫn ngoài `BrowseRoots` bằng 400. Giao diện phải hiện
   * thông báo, không im lặng trả về danh sách rỗng — im lặng khiến người dùng
   * tưởng thư mục trống thật.
   */
  it('hiện lỗi khi backend từ chối đường dẫn', async () => {
    stubBrowse({ '': roots })

    renderWithProviders(<FolderPicker value="C:\\Windows" onChange={vi.fn()} />)

    expect(await screen.findByText(/Không tìm thấy/)).toBeInTheDocument()
  })

  it('thư mục rỗng nói rõ là rỗng', async () => {
    stubBrowse({
      '': roots,
      'D:\\Du lieu': { path: 'D:\\Du lieu', parent: null, entries: [] },
    })

    renderWithProviders(<FolderPicker value={null} onChange={vi.fn()} />)
    await userEvent.click(await screen.findByLabelText('Mở D:\\Du lieu'))

    expect(await screen.findByText('Không có thư mục con.')).toBeInTheDocument()
  })
})

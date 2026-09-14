import { screen, waitFor, within } from '@testing-library/react'
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

/** Remote thật của rclone — tên phân biệt hoa thường. */
const remotes = [
  { name: 'hub', type: 'drive' },
  { name: 'hub-crypt', type: 'crypt' },
]

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
    if (url.includes('/api/backup/remotes')) {
      return Promise.resolve(Response.json(remotes))
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
  // Đích tách hai phần: chọn remote từ danh sách, gõ đường dẫn bên trong.
  await userEvent.selectOptions(await screen.findByLabelText('Remote'), 'hub')
  // Ô đích có sẵn tiền tố mặc định — xoá trước khi gõ, vì type() nối thêm.
  await userEvent.clear(screen.getByLabelText('Đích trên cloud'))
  await userEvent.type(screen.getByLabelText('Đích trên cloud'), 'backup/tai-lieu')
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
    await userEvent.type(screen.getByLabelText('Đường dẫn trên máy chạy hub'), 'E:\\Anh\\2026')
    await userEvent.selectOptions(await screen.findByLabelText('Remote'), 'hub')
    await userEvent.clear(screen.getByLabelText('Đích trên cloud'))
    await userEvent.type(screen.getByLabelText('Đích trên cloud'), 'backup/anh-cu')
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

    expect(screen.getByLabelText('Đường dẫn trên máy chạy hub')).toHaveValue('D:\\Du lieu')
  })

  /**
   * Lỗi đã gặp thật: gõ `Hub:backup` trong khi remote tên `hub`. rclone phân
   * biệt hoa thường nên job hỏng lúc CHẠY, và giao diện chỉ hiện "rclone thất
   * bại (mã 1)". Chọn từ danh sách thì không gõ sai tên được nữa.
   */
  it('ghép remote đã chọn với đường dẫn thành đích đầy đủ', async () => {
    const fetchMock = stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

    await waitFor(() => expect(savedBody(fetchMock)).toBeTruthy())

    expect(savedBody(fetchMock)).toMatchObject({ destination: 'hub:backup/tai-lieu' })
  })

  /**
   * Mọi job gom dưới một thư mục mẹ trên Drive thay vì rải ra gốc, lẫn với
   * thư mục cá nhân của người dùng.
   */
  it('điền sẵn thư mục mẹ HubBackup/ cho job mới', async () => {
    const fetchMock = stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)

    expect(await screen.findByLabelText('Đích trên cloud')).toHaveValue('HubBackup/')

    // Gõ tiếp phần tên là đủ, không phải tự gõ lại tiền tố.
    await userEvent.type(screen.getByLabelText('Tên'), 'anh')
    await userEvent.click(await screen.findByText('D:\\Du lieu'))
    await userEvent.selectOptions(screen.getByLabelText('Remote'), 'hub')
    await userEvent.type(screen.getByLabelText('Đích trên cloud'), 'anh')
    await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

    await waitFor(() => expect(savedBody(fetchMock)).toBeTruthy())
    expect(savedBody(fetchMock)).toMatchObject({ destination: 'hub:HubBackup/anh' })
  })

  it('liệt kê remote thật để chọn', async () => {
    stubApi()

    renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)

    const select = await screen.findByLabelText('Remote')
    expect(within(select).getByRole('option', { name: /hub \(drive\)/ })).toBeInTheDocument()
    expect(within(select).getByRole('option', { name: /hub-crypt \(crypt\)/ })).toBeInTheDocument()
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

    // `detail` mới là câu nói rõ chuyện gì; `title` chỉ là tên chung của mã HTTP
    // do ASP.NET tự điền. Hiện "Conflict" thì người dùng không biết phải sửa gì.
    expect(await screen.findByText(/Đã có công việc cùng tên/)).toBeInTheDocument()
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

  /**
   * Chế độ tải lên (§5d) — thứ duy nhất sao lưu được thư mục của MÁY KHÁC, vì
   * máy chủ không đọc được đĩa máy khác kể cả trong tailnet.
   */
  describe('tải thư mục từ máy đang mở web', () => {
    /** Giả lập thứ `<input webkitdirectory>` trả về: File có webkitRelativePath. */
    function fileWithPath(path: string, content: string): File {
      const file = new File([content], path.split('/').pop() ?? path)
      Object.defineProperty(file, 'webkitRelativePath', { value: path })
      return file
    }

    async function pickFolder(files: File[]) {
      await userEvent.click(screen.getByRole('button', { name: /Tải lên từ máy này/ }))
      const input = screen.getByLabelText('Chọn thư mục để tải lên')
      await userEvent.upload(input, files)
    }

    function uploadCalls(fetchMock: ReturnType<typeof stubApi>) {
      return fetchMock.mock.calls.filter(([input]) =>
        requestUrl(input).includes('/api/backup/upload'),
      )
    }

    it('gửi multipart kèm đường dẫn tương đối, giữ cấu trúc thư mục con', async () => {
      const fetchMock = stubApi()

      renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
      await pickFolder([
        fileWithPath('Anh/2026/img1.jpg', 'mot'),
        fileWithPath('Anh/ghi-chu.txt', 'hai'),
      ])

      await userEvent.selectOptions(await screen.findByLabelText('Remote'), 'hub')
      await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

      await waitFor(() => expect(uploadCalls(fetchMock).length).toBeGreaterThan(0))

      const body = uploadCalls(fetchMock)[0]?.[1]?.body
      // Stringify một FormData cho "{}" — mọi tệp biến mất mà không có lỗi nào.
      expect(body).toBeInstanceOf(FormData)

      const form = body as FormData
      expect(form.get('folder')).toBe('anh')
      // Đoạn đầu ("Anh/") đã bị cắt: nó thành tên thư mục trên hub rồi, giữ lại
      // sẽ lồng thêm một cấp thừa.
      expect(form.getAll('paths')).toEqual(['2026/img1.jpg', 'ghi-chu.txt'])
    })

    /**
     * Tạo job TRƯỚC rồi tải mà lỗi giữa chừng thì còn lại một job trỏ vào thư
     * mục thiếu tệp — bản sao lưu sai lệch mà không có gì báo.
     */
    it('chỉ tạo job sau khi tải xong, và gửi tên thư mục chứ không phải đường dẫn', async () => {
      const fetchMock = stubApi()

      renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
      await pickFolder([fileWithPath('Anh/img.jpg', 'x')])

      await userEvent.selectOptions(await screen.findByLabelText('Remote'), 'hub')
      await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

      await waitFor(() => expect(savedBody(fetchMock)).toBeTruthy())

      const order = fetchMock.mock.calls
        .map(([input], index) => ({ url: requestUrl(input), index }))
        .filter(
          (call) =>
            call.url.includes('/api/backup/upload') || call.url.includes('/api/backup/jobs'),
        )

      expect(order[0]?.url).toContain('/upload')
      expect(order.at(-1)?.url).toContain('/jobs')

      // Đường dẫn tuyệt đối không rời khỏi máy chủ: chỉ gửi TÊN thư mục, backend
      // tự dựng Source từ UploadRoot.
      expect(savedBody(fetchMock)).toMatchObject({ uploadFolder: 'anh', source: '' })
    })

    it('tải lỗi thì không tạo job và hiện lỗi', async () => {
      const fetchMock = vi.fn<typeof fetch>((input) => {
        const url = requestUrl(input)
        if (url.includes('/api/antiforgery/token')) {
          return Promise.resolve(Response.json({ token: 't', headerName: 'X-CSRF-Token' }))
        }
        if (url.includes('/api/backup/remotes')) return Promise.resolve(Response.json(remotes))
        if (url.includes('/api/backup/presets')) return Promise.resolve(Response.json(presets))
        if (url.includes('/api/backup/browse')) return Promise.resolve(Response.json(roots))
        if (url.includes('/api/backup/upload')) {
          return Promise.resolve(Response.json({ detail: 'Hết dung lượng đĩa.' }, { status: 400 }))
        }
        return Promise.resolve(Response.json({}))
      })
      vi.stubGlobal('fetch', fetchMock)

      renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
      await pickFolder([fileWithPath('Anh/img.jpg', 'x')])

      await userEvent.selectOptions(await screen.findByLabelText('Remote'), 'hub')
      await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

      expect(await screen.findByText(/Hết dung lượng đĩa/)).toBeInTheDocument()

      const jobCalls = fetchMock.mock.calls.filter(
        ([input, init]) =>
          (init?.method ?? 'GET') === 'POST' && requestUrl(input).includes('/api/backup/jobs'),
      )
      expect(jobCalls).toHaveLength(0)
    })

    it('gợi ý tên job từ tên thư mục đã chọn', async () => {
      stubApi()

      renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
      await pickFolder([fileWithPath('Tài liệu/a.txt', 'x')])

      // Bỏ dấu và hạ chữ thường: tên job chỉ nhận chữ, số, gạch ngang, gạch dưới.
      expect(screen.getByLabelText('Tên')).toHaveValue('tai-lieu')
    })

    it('vẫn tạo được job từ thư mục có sẵn trên máy chạy hub', async () => {
      const fetchMock = stubApi()

      renderWithProviders(<JobDialog open onOpenChange={vi.fn()} />)
      await fillRequiredFields()
      await userEvent.click(screen.getByRole('button', { name: 'Tạo công việc' }))

      await waitFor(() => expect(savedBody(fetchMock)).toBeTruthy())

      // Chế độ cũ không đụng tới đường tải lên.
      expect(uploadCalls(fetchMock)).toHaveLength(0)
      expect(savedBody(fetchMock)).toMatchObject({ source: 'D:\\Du lieu' })
    })
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

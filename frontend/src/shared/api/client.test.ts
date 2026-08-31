import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError, apiFetch, __resetCsrfTokenForTests } from './client'

/**
 * `fetch` nhận string | URL | Request. `String()` trên Request cho
 * "[object Object]", nên phải lấy URL đúng cách theo từng kiểu.
 */
function urlOf(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  if (input instanceof URL) return input.href
  return input.url
}

function mockFetch(response: Response) {
  // Request đổi trạng thái sẽ gọi thêm /api/antiforgery/token trước; trả token
  // giả cho lời gọi đó để test tập trung vào hành vi đang kiểm.
  const spy = vi
    .fn<typeof fetch>()
    .mockImplementation((input) =>
      urlOf(input) === '/api/antiforgery/token'
        ? Promise.resolve(Response.json({ token: 'tok-test', headerName: 'X-CSRF-Token' }))
        : Promise.resolve(response),
    )

  vi.stubGlobal('fetch', spy)
  return spy
}

afterEach(() => {
  vi.unstubAllGlobals()
})

beforeEach(() => {
  // Token được cache ở module scope; không reset thì test này ảnh hưởng test kia.
  __resetCsrfTokenForTests()
})

describe('apiFetch', () => {
  it('trả về body đã parse khi thành công', async () => {
    mockFetch(Response.json({ status: 'ok', utc: '2026-08-29T05:00:00Z' }))

    await expect(apiFetch('/health')).resolves.toEqual({
      status: 'ok',
      utc: '2026-08-29T05:00:00Z',
    })
  })

  it('luôn gửi kèm cookie — phiên là HttpOnly nên JS không tự đính kèm được', async () => {
    const spy = mockFetch(Response.json({ status: 'ok' }))

    await apiFetch('/health')

    expect(spy).toHaveBeenCalledWith('/health', expect.objectContaining({ credentials: 'include' }))
  })

  it('trả về undefined cho 204, không cố parse body rỗng', async () => {
    mockFetch(new Response(null, { status: 204 }))

    await expect(apiFetch('/api/auth/logout', { method: 'POST' })).resolves.toBeUndefined()
  })

  it('ném ApiError mang mã HTTP và title từ ProblemDetails', async () => {
    mockFetch(Response.json({ title: 'Mật khẩu không đúng.' }, { status: 401 }))

    const error = await apiFetch('/api/auth/login', { method: 'POST', body: {} }).catch(
      (caught: unknown) => caught,
    )

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 401, message: 'Mật khẩu không đúng.' })
    expect((error as ApiError).isUnauthorized).toBe(true)
  })

  it('rơi về thông báo theo mã HTTP khi body lỗi không phải JSON', async () => {
    // Xảy ra thật khi proxy chết: nó trả trang HTML, không phải ProblemDetails.
    mockFetch(new Response('<html>502 Bad Gateway</html>', { status: 502 }))

    const error = await apiFetch('/health').catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).message).toContain('502')
    expect((error as ApiError).isUnauthorized).toBe(false)
  })

  it('gửi JSON body kèm Content-Type khi có body', async () => {
    const spy = mockFetch(new Response(null, { status: 204 }))

    await apiFetch('/api/auth/login', { method: 'POST', body: { password: 'bí mật' } })

    expect(spy).toHaveBeenCalledWith(
      '/api/auth/login',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ password: 'bí mật' }),
        // Kèm cả CSRF token: POST là request đổi trạng thái (§6.5 mục 5).
        headers: expect.objectContaining({
          'Content-Type': 'application/json',
        }) as Record<string, string>,
      }),
    )
  })

  describe('CSRF (§6.5 mục 5)', () => {
    /** Giả lập backend: endpoint token trả token, các endpoint khác trả theo kịch bản. */
    function mockWithCsrf(handler: (url: string, init?: RequestInit) => Response) {
      const spy = vi.fn<typeof fetch>().mockImplementation((input, init) => {
        const url = urlOf(input)

        if (url === '/api/antiforgery/token') {
          return Promise.resolve(Response.json({ token: 'tok-1', headerName: 'X-CSRF-Token' }))
        }

        return Promise.resolve(handler(url, init))
      })

      vi.stubGlobal('fetch', spy)
      return spy
    }

    it('GET không lấy token — chỉ request đổi trạng thái mới cần', async () => {
      const spy = mockWithCsrf(() => Response.json({ status: 'ok' }))

      await apiFetch('/health')

      expect(spy).toHaveBeenCalledTimes(1)
      expect(spy).not.toHaveBeenCalledWith('/api/antiforgery/token', expect.anything())
    })

    it('POST gửi kèm header CSRF token', async () => {
      const spy = mockWithCsrf(() => new Response(null, { status: 204 }))

      await apiFetch('/api/devices/abc/lock', { method: 'POST' })

      const call = spy.mock.calls.find(([url]) => urlOf(url).endsWith('/lock'))
      expect(call).toBeDefined()
      expect((call?.[1]?.headers as Record<string, string>)['X-CSRF-Token']).toBe('tok-1')
    })

    it('chỉ lấy token một lần cho nhiều request', async () => {
      const spy = mockWithCsrf(() => new Response(null, { status: 204 }))

      await apiFetch('/api/devices/a/lock', { method: 'POST' })
      await apiFetch('/api/devices/b/lock', { method: 'POST' })

      const tokenCalls = spy.mock.calls.filter(([url]) => url === '/api/antiforgery/token')
      expect(tokenCalls).toHaveLength(1)
    })

    /**
     * Đăng nhập xoay session nên token lấy trước đó mất hiệu lực — đã gặp thật
     * lúc kiểm chứng bằng HTTP. Client phải tự lấy token mới và thử lại.
     */
    it('token hết hiệu lực trả 400 thì lấy token mới và thử lại một lần', async () => {
      let attempts = 0

      const spy = mockWithCsrf(() => {
        attempts += 1
        return attempts === 1
          ? Response.json({ title: 'Yêu cầu thiếu hoặc sai CSRF token.' }, { status: 400 })
          : new Response(null, { status: 204 })
      })

      await expect(apiFetch('/api/auth/logout', { method: 'POST' })).resolves.toBeUndefined()

      expect(attempts).toBe(2)
      const tokenCalls = spy.mock.calls.filter(([url]) => url === '/api/antiforgery/token')
      expect(tokenCalls).toHaveLength(2)
    })

    it('không thử lại vô hạn khi 400 lặp lại', async () => {
      let attempts = 0

      mockWithCsrf(() => {
        attempts += 1
        return Response.json({ title: 'Sai token.' }, { status: 400 })
      })

      await expect(apiFetch('/api/auth/logout', { method: 'POST' })).rejects.toBeInstanceOf(
        ApiError,
      )

      expect(attempts).toBe(2)
    })
  })
})

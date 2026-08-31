import type { components } from './schema'

export type { components }

/** Kiểu sinh từ OpenAPI (§3). Không viết tay. */
export type HealthResponse = components['schemas']['HealthResponse']

/**
 * Lỗi từ backend. Giữ mã HTTP để nơi gọi phân biệt được "chưa đăng nhập" (401)
 * với "hỏng thật" — hai thứ này cần hành xử khác nhau ở tầng UI.
 */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }

  /** Phiên hết hạn hoặc chưa đăng nhập — đưa người dùng về màn hình đăng nhập. */
  get isUnauthorized(): boolean {
    return this.status === 401
  }
}

/** Body của ProblemDetails (RFC 9457) mà ASP.NET Core trả về khi lỗi. */
interface ProblemDetails {
  title?: string
  detail?: string
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null
}

async function readErrorMessage(response: Response): Promise<string> {
  // Backend trả ProblemDetails cho lỗi nghiệp vụ, nhưng lỗi hạ tầng (proxy chết,
  // 502) trả HTML. Đọc JSON hỏng thì rơi về thông báo theo mã HTTP.
  try {
    const body: unknown = await response.json()
    if (isProblemDetails(body)) {
      const message = body.title ?? body.detail
      if (typeof message === 'string' && message.length > 0) {
        return message
      }
    }
  } catch {
    // Không phải JSON — dùng thông báo mặc định bên dưới.
  }

  return `Máy chủ trả lỗi ${response.status}.`
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'DELETE'
  body?: unknown
  signal?: AbortSignal
}

/**
 * CSRF token (§6.5 mục 5).
 *
 * Backend phát token qua /api/antiforgery/token: một nửa vào cookie, nửa còn lại
 * trả về đây để gửi kèm header. Trang khác không đọc được cookie của ta nên
 * không ghép đủ hai nửa — đó là cơ chế chống CSRF.
 *
 * Cache lại vì token dùng được nhiều lần; chỉ gọi lại khi chưa có hoặc khi
 * backend báo token hỏng.
 */
let csrfToken: string | null = null
let csrfHeaderName = 'X-CSRF-Token'
let pendingCsrfFetch: Promise<void> | null = null

async function ensureCsrfToken(): Promise<void> {
  if (csrfToken !== null) {
    return
  }

  // Nhiều request cùng lúc lúc mới mở trang: chỉ gọi endpoint token một lần.
  pendingCsrfFetch ??= (async () => {
    try {
      const response = await fetch('/api/antiforgery/token', { credentials: 'include' })
      if (!response.ok) {
        return
      }

      const body = (await response.json()) as { token?: string; headerName?: string }
      if (typeof body.token === 'string' && body.token.length > 0) {
        csrfToken = body.token
        if (typeof body.headerName === 'string' && body.headerName.length > 0) {
          csrfHeaderName = body.headerName
        }
      }
    } finally {
      pendingCsrfFetch = null
    }
  })()

  await pendingCsrfFetch
}

/** Buộc lấy token mới ở lần gọi sau. */
function invalidateCsrfToken(): void {
  csrfToken = null
}

/** Chỉ dùng trong test: token cache ở module scope nên phải reset giữa các test. */
export function __resetCsrfTokenForTests(): void {
  csrfToken = null
  pendingCsrfFetch = null
}

/**
 * Gọi API của hub.
 *
 * `credentials: 'include'` là bắt buộc: phiên đăng nhập nằm trong cookie HttpOnly
 * (§6.3), JavaScript không đọc được nó — trình duyệt phải tự đính kèm.
 */
export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, signal } = options

  // GET không đổi trạng thái nên không cần token; chỉ POST/DELETE mới cần.
  const needsCsrf = method !== 'GET'

  if (needsCsrf) {
    await ensureCsrfToken()
  }

  const send = async (): Promise<Response> => {
    const headers: Record<string, string> = {}

    if (body !== undefined) {
      headers['Content-Type'] = 'application/json'
    }

    if (needsCsrf && csrfToken !== null) {
      headers[csrfHeaderName] = csrfToken
    }

    return fetch(path, {
      method,
      signal,
      credentials: 'include',
      headers: Object.keys(headers).length === 0 ? undefined : headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  }

  let response = await send()

  // Token hết hạn (backend khởi động lại, cookie bị xoá) trả 400. Lấy token mới
  // và thử lại đúng MỘT lần — lặp vô hạn khi cấu hình sai là tự làm hỏng chính mình.
  if (needsCsrf && response.status === 400 && csrfToken !== null) {
    invalidateCsrfToken()
    await ensureCsrfToken()
    response = await send()
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorMessage(response))
  }

  // 204 No Content không có body; gọi .json() sẽ ném lỗi.
  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

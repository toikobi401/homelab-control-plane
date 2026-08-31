import { QueryClient } from '@tanstack/react-query'

import { ApiError } from './client'

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        // Thử lại lỗi mạng, nhưng không thử lại 4xx: sai mật khẩu hay hết phiên
        // thì thử lại bao nhiêu lần cũng vẫn sai, chỉ tổ chậm và tốn pin điện thoại.
        retry: (failureCount, error) => {
          if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
            return false
          }
          return failureCount < 2
        },
      },
      mutations: {
        retry: false,
      },
    },
  })
}

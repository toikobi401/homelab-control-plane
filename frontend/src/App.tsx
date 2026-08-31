import { QueryClientProvider } from '@tanstack/react-query'
import { useState } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { BackupPage } from '@/features/backup/BackupPage'
import { DevicesPage } from '@/features/devices/DevicesPage'
import { FilesPage } from '@/features/files/FilesPage'
import { HealthPage } from '@/features/health/HealthPage'
import { MangaPage } from '@/features/manga/MangaPage'
import { RemotePage } from '@/features/remote/RemotePage'
import { SessionsPage } from '@/features/auth/SessionsPage'
import { createQueryClient } from '@/shared/api/queryClient'
import { AppLayout } from '@/shared/components/AppLayout'
import { AuthGate } from '@/shared/components/AuthGate'
import { NotFoundPage } from '@/shared/components/NotFoundPage'

export function App() {
  // Tạo trong state, không phải module scope: mỗi lần mount (kể cả trong test)
  // cần một cache sạch, nếu không test này thấy dữ liệu của test kia.
  const [queryClient] = useState(createQueryClient)

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {/* AuthGate bọc ngoài router: chưa đăng nhập thì không route nào hiện,
            kể cả khi gõ thẳng đường dẫn. */}
        <AuthGate>
          <Routes>
            <Route element={<AppLayout />}>
              <Route index element={<HealthPage />} />
              <Route path="devices" element={<DevicesPage />} />
              <Route path="files" element={<FilesPage />} />
              <Route path="backup" element={<BackupPage />} />
              <Route path="remote" element={<RemotePage />} />
              <Route path="manga" element={<MangaPage />} />
              <Route path="sessions" element={<SessionsPage />} />
              <Route path="health" element={<Navigate to="/" replace />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Routes>
        </AuthGate>
      </BrowserRouter>
    </QueryClientProvider>
  )
}

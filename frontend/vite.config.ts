/// <reference types="vitest/config" />
import path from 'node:path'

import fs from 'node:fs'
import os from 'node:os'

import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

/** Backend .NET lúc dev — profile "Hub.Api (localhost)" trong launchSettings.json. */
const BACKEND_ORIGIN = process.env.HUB_BACKEND_ORIGIN ?? 'https://localhost:7189'

/**
 * Chứng chỉ dev do `dotnet dev-certs https` cấp, export sẵn ra thư mục của
 * ASP.NET. Thiếu nó thì dev server chạy HTTP và phiên đăng nhập sẽ không giữ
 * được — báo rõ cách sửa thay vì để người dùng tự đoán.
 */
function loadDevCert(): { key: Buffer; cert: Buffer } | undefined {
  // Chỗ `dotnet dev-certs` dùng: %APPDATA%\ASP.NET\https trên Windows,
  // ~/.aspnet/https trên Linux/macOS. Không hardcode đường dẫn Windows (§3.3).
  const dir = process.env.APPDATA
    ? path.join(process.env.APPDATA, 'ASP.NET', 'https')
    : path.join(os.homedir(), '.aspnet', 'https')

  const keyPath = path.join(dir, 'hub-dev.key')
  const certPath = path.join(dir, 'hub-dev.pem')

  if (!fs.existsSync(keyPath) || !fs.existsSync(certPath)) {
    console.warn(
      '[hub] Không tìm thấy chứng chỉ dev — dev server sẽ chạy HTTP và phiên đăng nhập KHÔNG giữ được.\n' +
        `      Sửa bằng: dotnet dev-certs https --export-path "${certPath}" --format Pem --no-password`,
    )
    return undefined
  }

  return { key: fs.readFileSync(keyPath), cert: fs.readFileSync(certPath) }
}

const devCert = loadDevCert()

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  server: {
    // Dev server PHẢI chạy HTTPS. Cookie phiên đặt SecurePolicy.Always (§4, §6.3),
    // nên qua HTTP thường trình duyệt không lưu nó: đăng nhập trả 200 rồi mọi
    // request sau vẫn 401 — một lỗi tốn thời gian vì trông như backend hỏng.
    // Chứng chỉ lấy từ `dotnet dev-certs`, không thêm dependency chỉ để có HTTPS.
    ...(devCert ? { https: devCert } : {}),

    // Đi qua proxy thay vì gọi thẳng cross-origin: lúc chạy thật backend tự phục
    // vụ file tĩnh nên frontend và API cùng một origin. Proxy giữ nguyên điều đó
    // lúc dev, để hành vi cookie SameSite=Strict giống hệt bản build.
    proxy: {
      // secure: false — chứng chỉ dev tự ký, Node từ chối nếu không tắt kiểm tra.
      '/api': { target: BACKEND_ORIGIN, changeOrigin: false, secure: false },
      '/health': { target: BACKEND_ORIGIN, changeOrigin: false, secure: false },
      '/openapi': { target: BACKEND_ORIGIN, changeOrigin: false, secure: false },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: true,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      exclude: ['src/shared/api/schema.ts', 'src/main.tsx', '**/*.test.{ts,tsx}'],
    },
  },
})

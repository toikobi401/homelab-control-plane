// Sinh kiểu TypeScript từ OpenAPI spec của backend (CONTEXT.md §3).
//
// Là script riêng chứ không phải một dòng trong package.json vì cần tắt kiểm tra
// chứng chỉ: backend lúc dev dùng chứng chỉ tự ký của `dotnet dev-certs`, Node
// từ chối nó. Chỉ ảnh hưởng tiến trình sinh code này, không lọt vào bản build.
import { writeFile } from 'node:fs/promises'
import { argv, exit } from 'node:process'

import openapiTS, { astToString } from 'openapi-typescript'

const SPEC_URL = process.env.HUB_OPENAPI_URL ?? 'https://localhost:7189/openapi/v1.json'
const OUTPUT = argv[2] ?? 'src/shared/api/schema.ts'

// Chỉ hợp lệ với localhost lúc dev. Không dùng cho bất cứ host nào khác.
const isLocalhost = /^https:\/\/localhost(:\d+)?\//.test(SPEC_URL)
if (isLocalhost) {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'
}

try {
  const ast = await openapiTS(new URL(SPEC_URL))
  await writeFile(OUTPUT, astToString(ast))
  console.log(`Đã sinh ${OUTPUT} từ ${SPEC_URL}`)
} catch (error) {
  console.error(
    `Không đọc được spec ở ${SPEC_URL}.\n` +
      'Backend phải đang chạy: dotnet run --project backend/Hub.Api --urls https://localhost:7189',
  )
  console.error(error)
  exit(1)
}

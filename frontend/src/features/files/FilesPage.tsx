import { NotBuiltYet } from '@/shared/components/NotBuiltYet'

export function FilesPage() {
  return (
    <NotBuiltYet
      title="Tệp"
      description="Duyệt tệp trên PC và laptop, và hàng đợi truyền giữa hai máy."
      blockedBy="Cần agent phục vụ SFTP và endpoint duyệt thư mục ở backend. Trình duyệt không đọc được tệp trong điện thoại (§2.1)."
    />
  )
}

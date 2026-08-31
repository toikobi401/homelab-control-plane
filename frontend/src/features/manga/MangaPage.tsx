import { NotBuiltYet } from '@/shared/components/NotBuiltYet'

export function MangaPage() {
  return (
    <NotBuiltYet
      title="Truyện tranh"
      description="Đọc truyện qua API công khai của MangaDex."
      blockedBy="Cần Hub.Manga và proxy ảnh ở backend — ISP chặn MangaDex ở tầng TCP, chỉ HTTP/3 đi được, nên trình duyệt không gọi thẳng được (§5)."
    />
  )
}

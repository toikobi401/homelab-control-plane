/**
 * Định dạng thời gian và dung lượng cho giao diện.
 *
 * Gom vào một chỗ vì hai trang cần cùng một cách nói: "2 giờ trước" ở trang
 * Thiết bị và ở trang Sao lưu phải ra cùng một chuỗi, nếu không người dùng sẽ
 * tưởng hai chỗ đo hai thứ khác nhau.
 */

/**
 * Khoảng cách từ một mốc thời gian tới bây giờ, nói theo lối thường ngày.
 *
 * Trả về mốc tuyệt đối khi đã quá một tháng: "37 ngày trước" bắt người đọc tự
 * nhẩm ra ngày, mà nhẩm sai thì tưởng bản sao lưu còn mới.
 */
export function describeRelativeTime(value: string | null): string {
  if (!value) {
    return 'Chưa từng chạy'
  }

  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return value
  }

  const seconds = Math.floor((Date.now() - parsed.getTime()) / 1000)

  // Đồng hồ máy khách lệch nhanh hơn máy chủ thì hiệu số âm. "Vừa xong" đúng
  // hơn là "-3 giây trước".
  if (seconds < 60) return 'Vừa xong'
  if (seconds < 3600) return `${Math.floor(seconds / 60)} phút trước`
  if (seconds < 86_400) return `${Math.floor(seconds / 3600)} giờ trước`
  if (seconds < 2_592_000) return `${Math.floor(seconds / 86_400)} ngày trước`

  return parsed.toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' })
}

/**
 * Dung lượng theo bội số 1024, đơn vị nhị phân.
 *
 * Dùng KiB/MiB/GiB chứ không phải KB/MB/GB: rclone đếm theo 1024, gọi tên theo
 * hệ thập phân sẽ lệch với con số nó báo trong log.
 */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '—'
  if (bytes === 0) return '0 B'

  const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB']
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1)
  const value = bytes / 1024 ** exponent

  // Số nguyên byte thì không cần phần thập phân; từ KiB trở lên lấy một chữ số
  // là đủ phân biệt mà không rối.
  const text = exponent === 0 ? String(bytes) : value.toFixed(value >= 100 ? 0 : 1)

  return `${text.replace('.', ',')} ${units[exponent]}`
}

/** Số tệp, chấm phân cách hàng nghìn theo lối Việt Nam. */
export function formatFileCount(files: number): string {
  if (!Number.isFinite(files) || files < 0) return '—'
  return `${files.toLocaleString('vi-VN')} tệp`
}

/**
 * Thời lượng một lần chạy, từ hai mốc ISO.
 *
 * Trả `null` khi chưa kết thúc — nơi gọi tự quyết định hiện gì, vì "đang chạy"
 * và "không rõ" là hai chuyện khác nhau.
 */
export function formatDuration(startedAt: string, finishedAt: string | null): string | null {
  if (!finishedAt) return null

  const start = new Date(startedAt).getTime()
  const end = new Date(finishedAt).getTime()
  if (Number.isNaN(start) || Number.isNaN(end)) return null

  const seconds = Math.max(0, Math.round((end - start) / 1000))

  if (seconds < 60) return `${seconds}s`

  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) {
    const rest = seconds % 60
    return rest === 0 ? `${minutes}m` : `${minutes}m ${rest}s`
  }

  const hours = Math.floor(minutes / 60)
  const restMinutes = minutes % 60
  return restMinutes === 0 ? `${hours}h` : `${hours}h ${String(restMinutes).padStart(2, '0')}m`
}

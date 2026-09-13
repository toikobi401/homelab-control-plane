import { afterEach, describe, expect, it, vi } from 'vitest'

import { describeRelativeTime, formatBytes, formatDuration, formatFileCount } from './time'

afterEach(() => {
  vi.useRealTimers()
})

describe('describeRelativeTime', () => {
  it('nói "Chưa từng chạy" khi không có mốc thời gian', () => {
    expect(describeRelativeTime(null)).toBe('Chưa từng chạy')
  })

  it('trả nguyên chuỗi khi không phân tích được', () => {
    expect(describeRelativeTime('không-phải-ngày')).toBe('không-phải-ngày')
  })

  it('gộp mọi thứ dưới một phút thành "Vừa xong"', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-01-01T12:00:00Z'))

    expect(describeRelativeTime('2026-01-01T11:59:30Z')).toBe('Vừa xong')
  })

  it('đồng hồ máy khách chạy nhanh hơn máy chủ vẫn ra "Vừa xong", không phải số âm', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-01-01T12:00:00Z'))

    expect(describeRelativeTime('2026-01-01T12:00:05Z')).toBe('Vừa xong')
  })

  it('đếm phút, giờ và ngày', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-01-10T12:00:00Z'))

    expect(describeRelativeTime('2026-01-10T11:30:00Z')).toBe('30 phút trước')
    expect(describeRelativeTime('2026-01-10T09:00:00Z')).toBe('3 giờ trước')
    expect(describeRelativeTime('2026-01-08T12:00:00Z')).toBe('2 ngày trước')
  })

  it('quá một tháng thì hiện mốc tuyệt đối, không bắt người đọc tự nhẩm', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-06-01T12:00:00Z'))

    const result = describeRelativeTime('2026-01-01T12:00:00Z')

    // `dateStyle: 'short'` viết tắt năm thành hai chữ số (1/1/26), đó là ý muốn:
    // ngắn gọn cho một cột hẹp. Điều cần khẳng định là nó KHÔNG còn nói "… trước".
    expect(result).not.toMatch(/trước$/)
    expect(result).toMatch(/1\/1\/26/)
  })
})

describe('formatBytes', () => {
  it('dùng bội số 1024 và đơn vị nhị phân, khớp cách rclone đếm', () => {
    expect(formatBytes(0)).toBe('0 B')
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(1024)).toBe('1,0 KiB')
    expect(formatBytes(1024 * 1024)).toBe('1,0 MiB')
  })

  it('bỏ phần thập phân khi số đã lớn', () => {
    expect(formatBytes(150 * 1024)).toBe('150 KiB')
  })

  it('trả gạch ngang cho giá trị vô nghĩa', () => {
    expect(formatBytes(-1)).toBe('—')
    expect(formatBytes(Number.NaN)).toBe('—')
  })
})

describe('formatFileCount', () => {
  it('phân cách hàng nghìn theo lối Việt Nam', () => {
    expect(formatFileCount(1204)).toBe('1.204 tệp')
  })
})

describe('formatDuration', () => {
  it('trả null khi lần chạy chưa kết thúc — "đang chạy" khác "không rõ"', () => {
    expect(formatDuration('2026-01-01T12:00:00Z', null)).toBeNull()
  })

  it('đếm giây, phút và giờ', () => {
    expect(formatDuration('2026-01-01T12:00:00Z', '2026-01-01T12:00:03Z')).toBe('3s')
    expect(formatDuration('2026-01-01T12:00:00Z', '2026-01-01T12:01:12Z')).toBe('1m 12s')
    expect(formatDuration('2026-01-01T12:00:00Z', '2026-01-01T14:00:00Z')).toBe('2h')
  })

  it('không trả thời lượng âm khi hai mốc lệch nhau', () => {
    expect(formatDuration('2026-01-01T12:00:05Z', '2026-01-01T12:00:00Z')).toBe('0s')
  })
})

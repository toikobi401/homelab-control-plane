import { Lock, Moon, Power, RotateCcw, type LucideIcon } from 'lucide-react'

import type { PowerAction } from '@/shared/api/deviceControl'

interface PowerActionLabel {
  /** Nhãn nút và tiêu đề hộp thoại xác nhận. */
  verb: string
  icon: LucideIcon
  /**
   * Hậu quả, nói thẳng. Người dùng phải biết mình sắp làm gì trước khi bấm —
   * "Bạn có chắc không?" không nói lên điều gì.
   */
  consequence: string
  /** Hành động phá huỷ cần cảnh báo mạnh hơn (§5a). */
  destructive: boolean
}

export const POWER_ACTION_LABELS: Record<PowerAction, PowerActionLabel> = {
  shutdown: {
    verb: 'Tắt máy',
    icon: Power,
    consequence:
      'Máy sẽ tắt hẳn. Mọi việc chưa lưu sẽ mất, và chỉ bật lại được nếu phần cứng hỗ trợ đánh thức từ xa.',
    destructive: true,
  },
  restart: {
    verb: 'Khởi động lại',
    icon: RotateCcw,
    consequence: 'Máy sẽ khởi động lại ngay. Mọi việc chưa lưu sẽ mất.',
    destructive: true,
  },
  sleep: {
    verb: 'Cho ngủ',
    icon: Moon,
    consequence: 'Máy chuyển sang chế độ ngủ. Đánh thức lại được nếu phần cứng hỗ trợ.',
    destructive: true,
  },
  lock: {
    verb: 'Khoá màn hình',
    icon: Lock,
    consequence: 'Khoá màn hình, cần mật khẩu để dùng lại. Việc đang chạy vẫn tiếp tục.',
    destructive: false,
  },
}

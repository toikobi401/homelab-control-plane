import { Activity, BookOpen, Power, Server, ShieldCheck, Upload } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

export interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  /** Chỉ hiện ở sidebar desktop — thanh dưới cùng của điện thoại không đủ chỗ. */
  desktopOnly?: boolean
}

/**
 * Điều hướng chính. Mỗi mục ứng với một năng lực ở CONTEXT.md §1.
 *
 * Một danh sách dùng chung cho cả thanh dưới cùng (điện thoại) và sidebar
 * (desktop): hai nơi khai riêng là nguồn sai lệch, thêm năng lực mới sẽ quên
 * một chỗ.
 */
export const navItems: readonly NavItem[] = [
  // Trạng thái hub là trang gốc. Trên điện thoại nó không nằm trong thanh dưới
  // cùng — năm mục đã là giới hạn của màn hình hẹp, và chấm trạng thái ở thanh
  // tiêu đề đã nói đủ. Desktop rộng chỗ nên hiện thành mục riêng.
  { to: '/', label: 'Trạng thái', icon: Activity, desktopOnly: true },
  { to: '/devices', label: 'Thiết bị', icon: Server },
  // Không có mục "Tệp": MeshCentral đã có sẵn duyệt và truyền file, nên năng
  // lực 2 được phục vụ ở tab Điều khiển (§2.3: tái sử dụng thay vì tự viết).
  { to: '/backup', label: 'Sao lưu', icon: Upload },
  // Năng lực 6, nhúng MeshCentral. Nó lo cả điều khiển nguồn lẫn điều khiển
  // màn hình, nên năng lực 4 coi như được phục vụ luôn ở đây (§2.3: tái sử
  // dụng thay vì tự viết).
  { to: '/remote', label: 'Điều khiển', icon: Power },
  { to: '/manga', label: 'Truyện', icon: BookOpen },
  // Quản lý phiên là việc thỉnh thoảng mới làm — không xứng một ô trong năm ô
  // của thanh dưới cùng, nhưng sidebar desktop thì rộng chỗ.
  { to: '/sessions', label: 'Phiên', icon: ShieldCheck, desktopOnly: true },
]

/** Các mục hiện trên thanh dưới cùng của điện thoại. */
export const mobileNavItems = navItems.filter((item) => !item.desktopOnly)

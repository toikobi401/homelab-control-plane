import { Apple, Laptop, type LucideProps, Monitor, Server, Smartphone } from 'lucide-react'

/**
 * Biểu tượng theo hệ điều hành Tailscale báo về.
 *
 * Chuỗi `operatingSystem` do Tailscale đặt: "windows", "linux", "macOS", "iOS",
 * "android". So khớp không phân biệt hoa thường, và có mặc định cho giá trị lạ —
 * danh sách này có thể dài ra mà ta không biết trước.
 */
export function DeviceIcon({
  operatingSystem,
  ...props
}: { operatingSystem: string } & LucideProps) {
  const os = operatingSystem.toLowerCase()

  if (os.includes('ios') || os.includes('android')) {
    return <Smartphone {...props} aria-hidden="true" />
  }

  if (os.includes('mac')) {
    return <Apple {...props} aria-hidden="true" />
  }

  if (os.includes('windows')) {
    return <Laptop {...props} aria-hidden="true" />
  }

  if (os.includes('linux')) {
    return <Server {...props} aria-hidden="true" />
  }

  return <Monitor {...props} aria-hidden="true" />
}

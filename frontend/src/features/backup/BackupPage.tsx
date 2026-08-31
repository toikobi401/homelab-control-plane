import { NotBuiltYet } from '@/shared/components/NotBuiltYet'

export function BackupPage() {
  return (
    <NotBuiltYet
      title="Sao lưu"
      description="Job sao lưu lên cloud và lịch sử các lần chạy."
      blockedBy="Cần chốt nhà cung cấp cloud và cấu hình rclone (§12 câu 4), rồi mới có endpoint để gọi."
    />
  )
}

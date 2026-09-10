namespace Hub.Core.Backup;

/// <summary>Trạng thái một lần chạy sao lưu.</summary>
public enum BackupRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,

    /// <summary>Bị huỷ vì quá thời gian, hoặc hub tắt giữa chừng.</summary>
    Cancelled = 3
}

/// <summary>
/// Một lần chạy sao lưu — lưu vào DB để xem lịch sử.
///
/// §6.5 mục 4 cấm log đường dẫn file. Nên bản ghi này giữ **số lượng** và
/// **dung lượng**, không giữ tên từng file. Muốn biết chi tiết thì xem log của
/// chính rclone.
/// </summary>
public sealed class BackupRun
{
    public int Id { get; set; }

    /// <summary>Khớp <see cref="BackupJobOptions.Name"/>.</summary>
    public string JobName { get; set; } = string.Empty;

    public BackupRunStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Số file đã truyền.</summary>
    public long FilesTransferred { get; set; }

    /// <summary>Tổng byte đã truyền.</summary>
    public long BytesTransferred { get; set; }

    /// <summary>Số lỗi rclone báo.</summary>
    public long Errors { get; set; }

    /// <summary>
    /// Thông báo lỗi ngắn gọn khi thất bại.
    ///
    /// §6.5 mục 7: chi tiết vào log, người dùng chỉ thấy câu chung. Trường này
    /// là câu chung đó — không chứa đường dẫn hay stack trace.
    /// </summary>
    public string? ErrorMessage { get; set; }

    public TimeSpan? Duration =>
        FinishedAt.HasValue ? FinishedAt.Value - StartedAt : null;
}

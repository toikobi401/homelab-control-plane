using Hub.Core.Results;

namespace Hub.Core.Backup;

/// <summary>
/// Chạy một công việc sao lưu.
///
/// Tách interface để <see cref="BackupService"/> không phụ thuộc vào việc gọi
/// tiến trình ngoài — Hub.Core không được biết gì về Windows (§3, luật phụ
/// thuộc). Bản cài đặt thật nằm ở Hub.Api.
/// </summary>
public interface IBackupRunner
{
    /// <summary>
    /// Chạy rclone cho một job, trả về thống kê khi xong.
    ///
    /// Không ném exception cho lỗi nghiệp vụ (rclone trả mã khác 0, đích không
    /// tồn tại) — trả <see cref="Result"/> thất bại. Exception chỉ dành cho
    /// việc không chạy nổi tiến trình.
    /// </summary>
    Task<Result<BackupRunStats>> RunAsync(
        BackupJobOptions job,
        CancellationToken cancellationToken);

    /// <summary>
    /// rclone có gọi được không, và phiên bản nào.
    ///
    /// Dùng cho màn hình trạng thái: thiếu rclone là lỗi cấu hình của người
    /// vận hành, phải nói rõ ngay chứ không đợi tới lúc chạy job mới báo.
    /// </summary>
    Task<Result<string>> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>Thống kê rclone trả về sau một lần chạy.</summary>
public readonly record struct BackupRunStats(
    long FilesTransferred,
    long BytesTransferred,
    long Errors);

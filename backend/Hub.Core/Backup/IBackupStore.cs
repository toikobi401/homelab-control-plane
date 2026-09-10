namespace Hub.Core.Backup;

/// <summary>
/// Lịch sử các lần sao lưu. Hiện thực ở Hub.Data (EF Core + SQLite) — Core
/// không biết gì về EF (§3, luật phụ thuộc).
/// </summary>
public interface IBackupStore
{
    /// <summary>Ghi lại một lần chạy vừa bắt đầu, trả về bản ghi có Id.</summary>
    Task<BackupRun> StartRunAsync(BackupRun run, CancellationToken cancellationToken = default);

    /// <summary>Cập nhật khi lần chạy kết thúc.</summary>
    Task FinishRunAsync(BackupRun run, CancellationToken cancellationToken = default);

    /// <summary>Lịch sử, mới nhất trước.</summary>
    Task<IReadOnlyList<BackupRun>> GetRunsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Lần chạy gần nhất của một job, để hiển thị trạng thái.</summary>
    Task<BackupRun?> GetLatestRunAsync(
        string jobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu mọi lần chạy còn treo ở trạng thái Running thành Cancelled.
    ///
    /// Gọi lúc hub khởi động: nếu máy tắt giữa chừng thì bản ghi kẹt ở Running
    /// mãi mãi, và giao diện sẽ hiện "đang chạy" cho một tiến trình đã chết.
    /// </summary>
    Task<int> CancelStaleRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>Xoá bản ghi cũ, giữ lại <paramref name="keep"/> bản gần nhất.</summary>
    Task<int> TrimHistoryAsync(int keep, CancellationToken cancellationToken = default);
}

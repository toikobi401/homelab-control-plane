using Hub.Core.Results;

namespace Hub.Core.Backup;

/// <summary>
/// Nơi lưu các công việc sao lưu do người dùng tạo từ giao diện.
///
/// **Vì sao không ghi vào <c>appsettings.Production.json</c>** (khác §5d, xem
/// nhật ký quyết định): file đó đang giữ token Tailscale, đường dẫn chứng chỉ,
/// và cấu hình MeshCentral. Một lỗi trong code ghi sẽ làm hỏng toàn bộ cấu hình
/// hub — và đã kiểm chứng thực tế: file JSON sai cú pháp thì hub bỏ qua **toàn
/// bộ** nội dung, mất luôn cả MeshCentral lẫn Tailscale.
///
/// Tách ra file riêng thì hỏng cũng chỉ mất danh sách job, và code ghi không
/// bao giờ chạm vào bí mật.
///
/// Job khai tay trong <c>appsettings</c> vẫn hoạt động như cũ — hai nguồn gộp
/// lại, xem <see cref="BackupService"/>.
/// </summary>
public interface IBackupJobStore
{
    /// <summary>Các job do người dùng tạo. Lỗi đọc file trả danh sách rỗng, không ném.</summary>
    Task<IReadOnlyList<BackupJobOptions>> GetJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm hoặc cập nhật một job theo tên. Tên trùng với job khai tay trong
    /// <c>appsettings</c> thì từ chối — nếu không sẽ có hai job cùng tên mà chỉ
    /// một cái chạy được.
    /// </summary>
    Task<Result> SaveJobAsync(BackupJobOptions job, CancellationToken cancellationToken = default);

    /// <summary>Xoá một job. Trả false nếu không có job nào tên như vậy.</summary>
    Task<bool> DeleteJobAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi nội dung file filter cho một job, trả về đường dẫn đã ghi.
    ///
    /// File đặt cạnh thư mục nguồn — giống cách <c>.gitignore</c> nằm cạnh code
    /// nó áp dụng (§5d). Nội dung do người dùng soạn, .NET **không parse**, chỉ
    /// ghi nguyên văn rồi để rclone tự đọc qua <c>--filter-from</c>.
    /// </summary>
    Task<Result<string>> WriteFilterFileAsync(
        string sourceDirectory,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>Đọc nội dung file filter. Không có file thì trả chuỗi rỗng.</summary>
    Task<string> ReadFilterFileAsync(
        string? filterFilePath,
        CancellationToken cancellationToken = default);
}

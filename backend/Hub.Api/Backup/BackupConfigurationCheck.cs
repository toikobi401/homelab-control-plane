using Hub.Core.Backup;

namespace Hub.Api.Backup;

/// <summary>
/// Kiểm tra cấu hình sao lưu lúc khởi động.
///
/// Sai cấu hình sao lưu là loại lỗi im lặng: mọi thứ chạy bình thường, cho tới
/// ngày cần khôi phục mới biết bản sao lưu thiếu hoặc lộ. Nói ra ngay lúc khởi
/// động rẻ hơn nhiều.
/// </summary>
public static class BackupConfigurationCheck
{
    public static void Validate(
        BackupOptions options,
        string dataDirectory,
        ILogger logger)
    {
        if (!options.IsConfigured)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in options.Jobs)
        {
            if (string.IsNullOrWhiteSpace(job.Name))
            {
                logger.LogError("Backup: có công việc không đặt tên — sẽ không gọi được qua API.");
                continue;
            }

            // Tên trùng thì API chỉ thấy cái đầu tiên, cái sau bị che hoàn toàn.
            if (!names.Add(job.Name))
            {
                logger.LogError(
                    "Backup: tên công việc {JobName} bị trùng — chỉ mục đầu tiên có tác dụng.",
                    job.Name);
            }

            if (string.IsNullOrWhiteSpace(job.Source) || string.IsNullOrWhiteSpace(job.Destination))
            {
                logger.LogError(
                    "Backup: công việc {JobName} thiếu Source hoặc Destination.", job.Name);
                continue;
            }

            if (!job.Enabled) { continue; }

            if (!Directory.Exists(job.Source) && !File.Exists(job.Source))
            {
                // Cảnh báo chứ không chặn: thư mục có thể là ổ ngoài chưa cắm.
                logger.LogWarning(
                    "Backup: nguồn của công việc {JobName} hiện không tồn tại.", job.Name);
            }

            WarnIfSensitiveAndUnencrypted(job, dataDirectory, logger);
        }
    }

    /// <summary>
    /// Cảnh báo khi sao lưu thư mục dữ liệu của hub lên đích KHÔNG mã hoá.
    ///
    /// hub.db chứa hash mật khẩu và phiên đăng nhập. Người dùng đã chọn lưu
    /// nguyên bản cho dữ liệu thường (để xem trực tiếp trên Drive), nhưng riêng
    /// file này thì phải đi qua remote crypt — quyết định đó dễ bị quên khi
    /// thêm job mới sau này.
    /// </summary>
    private static void WarnIfSensitiveAndUnencrypted(
        BackupJobOptions job,
        string dataDirectory,
        ILogger logger)
    {
        if (job.Encrypted) { return; }

        string source;
        string data;
        try
        {
            source = Path.GetFullPath(job.Source);
            data = Path.GetFullPath(dataDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Đường dẫn không chuẩn hoá được (ví dụ "gdrive:x" của rclone).
            // Không phải nguồn cục bộ nên không cần kiểm tra tiếp.
            return;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var overlaps = source.Equals(data, comparison)
            || source.StartsWith(data + Path.DirectorySeparatorChar, comparison)
            || data.StartsWith(source + Path.DirectorySeparatorChar, comparison);

        if (overlaps)
        {
            // §6.5 mục 4 cấm log đường dẫn — nêu tên job, không nêu đường dẫn.
            logger.LogError(
                "Backup: công việc {JobName} sao lưu dữ liệu của hub lên đích KHÔNG mã hoá. " +
                "hub.db chứa hash mật khẩu và phiên đăng nhập — dùng remote crypt cho job này.",
                job.Name);
        }
    }
}

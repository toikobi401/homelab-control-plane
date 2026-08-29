namespace Hub.Core.Configuration;

/// <summary>
/// Đường dẫn tới nơi lưu dữ liệu của hệ thống (file SQLite, log, dữ liệu tạm).
///
/// CONTEXT.md §3.3: không hardcode đường dẫn Windows, đọc từ cấu hình. Đây là
/// thứ cho phép cùng một binary chạy trên PC Windows lẫn container trên NAS.
/// </summary>
public static class HubPaths
{
    /// <summary>Tên biến môi trường / khoá cấu hình trỏ tới thư mục dữ liệu.</summary>
    public const string DataDirectoryKey = "HUB_DATA_DIR";

    /// <summary>
    /// Thư mục dữ liệu. Ưu tiên <c>HUB_DATA_DIR</c>; không có thì rơi về thư mục
    /// dữ liệu ứng dụng của người dùng — hợp lệ trên cả Windows lẫn Linux.
    ///
    /// Trong container ta luôn đặt <c>HUB_DATA_DIR=/data</c> và mount volume vào đó,
    /// nên chuyển sang NAS chỉ là trỏ volume sang share khác.
    /// </summary>
    public static string ResolveDataDirectory(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        return Path.Combine(baseDirectory, "Hub");
    }

    /// <summary>Đường dẫn file SQLite chính.</summary>
    public static string ResolveDatabasePath(string? configuredPath)
    {
        return Path.Combine(ResolveDataDirectory(configuredPath), "hub.db");
    }
}

using Hub.Core.Results;

namespace Hub.Core.Backup;

/// <summary>
/// Duyệt thư mục trên máy chạy hub, để giao diện chọn thư mục cần sao lưu.
///
/// **Đổi từ danh sách cho phép sang danh sách chặn (2026-09-13).** Bản đầu chỉ
/// cho duyệt trong một danh sách thư mục đã khai; người dùng cần chọn bất kỳ nên
/// giờ duyệt được mọi ổ đĩa cố định, chỉ chặn vài chỗ nhạy cảm.
///
/// Đây là **nới lỏng có ý thức**, không phải sơ suất. Hệ thống đã mở ra Internet
/// (§4a) và hub chạy dưới LocalSystem, nên ai chiếm được phiên đăng nhập sẽ đọc
/// được cấu trúc ổ đĩa. Cái giữ lại:
///
/// 1. **Chặn thư mục nhạy cảm** — Windows, Program Files, và thư mục dữ liệu
///    của hub (chứa <c>hub.db</c> với hash mật khẩu, và
///    <c>appsettings.Production.json</c> với token Tailscale). Đây đều là chỗ
///    không ai sao lưu mà lại lộ nhiều nhất.
/// 2. **Chuẩn hoá đường dẫn trước khi kiểm tra** — <c>Path.GetFullPath</c> giải
///    hết <c>..</c>, nên không vòng vào thư mục bị chặn bằng
///    <c>D:\x\..\..\Windows</c>.
///
/// Không đọc nội dung file, chỉ liệt kê tên thư mục.
/// </summary>
public sealed class DirectoryBrowser(BackupOptions options, string dataDirectory)
{
    /// <summary>Số mục tối đa trả về một lần, chặn thư mục khổng lồ làm nghẽn.</summary>
    private const int MaxEntries = 500;

    /// <summary>Mọi ổ đĩa cố định — điểm bắt đầu khi chưa chọn gì.</summary>
    public IReadOnlyList<string> GetRoots() =>
        [.. DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)];

    /// <summary>
    /// Các thư mục bị chặn, đã chuẩn hoá.
    ///
    /// Khai <c>BlockedPaths</c> thì thay hẳn mặc định — người vận hành tự chịu
    /// trách nhiệm cho danh sách của mình, không gộp ngầm với mặc định rồi để
    /// họ ngạc nhiên vì một thư mục vẫn bị chặn.
    /// </summary>
    private IReadOnlyList<string> GetBlockedPaths()
    {
        if (options.BlockedPaths.Count > 0)
        {
            return [.. options.BlockedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeOrNull)
                .OfType<string>()];
        }

        var defaults = new List<string>();

        void AddFolder(Environment.SpecialFolder folder)
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(path)) { defaults.Add(path); }
        }

        AddFolder(Environment.SpecialFolder.Windows);
        AddFolder(Environment.SpecialFolder.ProgramFiles);
        AddFolder(Environment.SpecialFolder.ProgramFilesX86);

        // Thư mục dữ liệu của hub: hub.db chứa hash mật khẩu và phiên đăng nhập,
        // appsettings.Production.json chứa token Tailscale. Đường dẫn này đến từ
        // HUB_DATA_DIR lúc chạy nên phải truyền vào, không hardcode được.
        if (!string.IsNullOrWhiteSpace(dataDirectory)) { defaults.Add(dataDirectory); }

        return [.. defaults.Select(NormalizeOrNull).OfType<string>()];
    }

    /// <summary>
    /// Liệt kê thư mục con của <paramref name="path"/>. Để trống thì trả danh
    /// sách ổ đĩa.
    /// </summary>
    public Result<DirectoryListing> List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var roots = GetRoots();
            return Result.Success(new DirectoryListing(
                Path: null,
                Parent: null,
                Entries: [.. roots.Select(root => new DirectoryEntry(root, root))]));
        }

        var full = NormalizeOrNull(path);
        if (full is null)
        {
            return Result.Failure<DirectoryListing>(ResultError.Validation(
                "Đường dẫn không hợp lệ."));
        }

        if (IsBlocked(full))
        {
            return Result.Failure<DirectoryListing>(ResultError.Validation(
                "Thư mục này bị chặn vì chứa dữ liệu hệ thống hoặc dữ liệu của hub."));
        }

        if (!Directory.Exists(full))
        {
            return Result.Failure<DirectoryListing>(ResultError.Validation(
                "Thư mục không tồn tại."));
        }

        List<DirectoryEntry> entries;
        try
        {
            entries = [.. Directory.EnumerateDirectories(full)
                .Where(child => !IsBlocked(child))
                .Take(MaxEntries)
                .Select(child => new DirectoryEntry(
                    Name: Path.GetFileName(child),
                    Path: child))
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (UnauthorizedAccessException)
        {
            // Thư mục hệ thống không đọc được là chuyện bình thường, không phải
            // sự cố — trả danh sách rỗng thay vì ném lỗi.
            entries = [];
        }
        catch (IOException)
        {
            return Result.Failure<DirectoryListing>(ResultError.Validation(
                "Không đọc được thư mục này."));
        }

        // Cha là null khi đang ở gốc ổ đĩa — lúc đó "lên trên" quay về danh sách
        // ổ đĩa, không phải một thư mục nào.
        var parent = Path.GetDirectoryName(full);

        return Result.Success(new DirectoryListing(full, parent, entries));
    }

    /// <summary>
    /// Đường dẫn này có dùng làm nguồn sao lưu được không.
    ///
    /// Gọi trước khi lưu job: người dùng gõ thẳng đường dẫn vào ô nhập, hoặc gửi
    /// lên API mà không qua bước duyệt.
    /// </summary>
    public Result<string> ValidateSource(string path)
    {
        var full = NormalizeOrNull(path);
        if (full is null)
        {
            return Result.Failure<string>(ResultError.Validation("Đường dẫn không hợp lệ."));
        }

        if (IsBlocked(full))
        {
            return Result.Failure<string>(ResultError.Validation(
                "Thư mục này bị chặn vì chứa dữ liệu hệ thống hoặc dữ liệu của hub."));
        }

        if (!Directory.Exists(full))
        {
            return Result.Failure<string>(ResultError.Validation("Thư mục nguồn không tồn tại."));
        }

        return Result.Success(full);
    }

    private static string? NormalizeOrNull(string path)
    {
        try
        {
            // GetFullPath giải hết ".." và "." — nhờ đó không vòng vào thư mục
            // bị chặn bằng "D:\x\..\..\Windows".
            var full = Path.GetFullPath(path.Trim());
            return full.Length > 1 ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Đường dẫn có nằm trong (hoặc chính là) một thư mục bị chặn không.
    ///
    /// Chặn cả thư mục CHA của vùng bị chặn thì quá tay — chặn
    /// <c>D:\App\HubData</c> không có nghĩa là cấm luôn <c>D:\App</c>, vì người
    /// dùng có thể muốn sao lưu phần còn lại của <c>D:\App</c>.
    /// </summary>
    private bool IsBlocked(string fullPath)
    {
        var normalized = NormalizeOrNull(fullPath);
        if (normalized is null) { return true; }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var blocked in GetBlockedPaths())
        {
            if (normalized.Equals(blocked, comparison))
            {
                return true;
            }

            // So kèm dấu phân cách: "D:\App" và "D:\AppData" là hai thư mục
            // khác nhau, StartsWith trần sẽ coi cái sau nằm trong cái trước.
            var prefix = blocked.EndsWith(Path.DirectorySeparatorChar)
                ? blocked
                : blocked + Path.DirectorySeparatorChar;

            if (normalized.StartsWith(prefix, comparison))
            {
                return true;
            }
        }

        return false;
    }
}

/// <param name="Path">Thư mục đang xem; <c>null</c> khi đang ở danh sách ổ đĩa.</param>
/// <param name="Parent">Thư mục cha; <c>null</c> khi đang ở gốc ổ đĩa.</param>
public sealed record DirectoryListing(
    string? Path,
    string? Parent,
    IReadOnlyList<DirectoryEntry> Entries);

public sealed record DirectoryEntry(string Name, string Path);

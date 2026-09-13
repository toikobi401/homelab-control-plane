using Hub.Core.Results;

namespace Hub.Core.Backup;

/// <summary>
/// Duyệt thư mục trên máy chạy hub, để giao diện chọn thư mục cần sao lưu.
///
/// Đây là **bề mặt tấn công mới**: hệ thống đã mở ra Internet (§4a), nên một
/// endpoint liệt kê thư mục nghĩa là ai chiếm được phiên đăng nhập đều đọc được
/// cấu trúc ổ đĩa. Hai lớp phòng thủ:
///
/// 1. **Giới hạn gốc** (<see cref="BackupOptions.BrowseRoots"/>) — chỉ đi được
///    trong các thư mục đã khai. Khác §5c: ở đó có một `LibraryPath` duy nhất,
///    ở đây mục đích là duyệt tự do nên phải có danh sách gốc rõ ràng.
/// 2. **Chống path traversal** — chuẩn hoá bằng <c>Path.GetFullPath</c> rồi
///    kiểm tra nằm trong gốc, đúng cách §5c quy định. Chuỗi
///    <c>../../../Windows/System32</c> không đi xa hơn bước này.
///
/// Không đọc nội dung file, chỉ liệt kê tên thư mục — thứ ít nhất cần để chọn
/// thư mục sao lưu.
/// </summary>
public sealed class DirectoryBrowser(BackupOptions options)
{
    /// <summary>Số mục tối đa trả về một lần, chặn thư mục khổng lồ làm nghẽn.</summary>
    private const int MaxEntries = 500;

    /// <summary>
    /// Các gốc được phép duyệt, đã chuẩn hoá. Không khai gì thì rơi về mọi ổ
    /// đĩa cố định — tiện lúc bắt đầu, nhưng khai tường minh vẫn hẹp hơn.
    /// </summary>
    public IReadOnlyList<string> GetRoots()
    {
        if (options.BrowseRoots.Count > 0)
        {
            return [.. options.BrowseRoots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(NormalizeOrNull)
                .OfType<string>()
                .Where(Directory.Exists)];
        }

        return [.. DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)];
    }

    /// <summary>
    /// Liệt kê thư mục con của <paramref name="path"/>. Để trống thì trả danh
    /// sách gốc.
    /// </summary>
    public Result<DirectoryListing> List(string? path)
    {
        var roots = GetRoots();

        if (roots.Count == 0)
        {
            return Result.Failure<DirectoryListing>(ResultError.Validation(
                "Chưa khai thư mục nào được phép duyệt (Backup:BrowseRoots)."));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
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

        if (!IsInsideAnyRoot(full, roots))
        {
            // Không nói rõ "nằm ngoài gốc nào" — đó là thông tin về cấu trúc máy
            // mà người gọi chưa được phép biết.
            return Result.Failure<DirectoryListing>(ResultError.Validation(
                "Thư mục này không nằm trong phạm vi được phép duyệt."));
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

        // Chỉ cho lên cha khi cha vẫn nằm trong gốc — không để leo ra ngoài
        // bằng cách bấm "lên trên" liên tục.
        var parent = Path.GetDirectoryName(full);
        if (parent is not null && !IsInsideAnyRoot(parent, roots))
        {
            parent = null;
        }

        return Result.Success(new DirectoryListing(full, parent, entries));
    }

    /// <summary>
    /// Đường dẫn này có được phép dùng làm nguồn sao lưu không.
    ///
    /// Gọi trước khi lưu job: người dùng có thể gửi thẳng đường dẫn bất kỳ lên
    /// API mà không qua bước duyệt.
    /// </summary>
    public Result<string> ValidateSource(string path)
    {
        var full = NormalizeOrNull(path);
        if (full is null)
        {
            return Result.Failure<string>(ResultError.Validation("Đường dẫn không hợp lệ."));
        }

        if (!IsInsideAnyRoot(full, GetRoots()))
        {
            return Result.Failure<string>(ResultError.Validation(
                "Thư mục nguồn không nằm trong phạm vi được phép."));
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
            // GetFullPath giải hết ".." và "." — đây là bước chống path traversal.
            var full = Path.GetFullPath(path.Trim());
            return full.Length > 1 ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsInsideAnyRoot(string fullPath, IReadOnlyList<string> roots)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var root in roots)
        {
            if (fullPath.Equals(root, comparison))
            {
                return true;
            }

            // Phải so kèm dấu phân cách: "D:\App" và "D:\AppData" khác nhau,
            // nhưng StartsWith trần sẽ coi cái sau nằm trong cái trước.
            var prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(prefix, comparison))
            {
                return true;
            }
        }

        return false;
    }
}

/// <param name="Path">Thư mục đang xem; <c>null</c> khi đang ở danh sách gốc.</param>
/// <param name="Parent">Thư mục cha nếu còn trong phạm vi cho phép.</param>
public sealed record DirectoryListing(
    string? Path,
    string? Parent,
    IReadOnlyList<DirectoryEntry> Entries);

public sealed record DirectoryEntry(string Name, string Path);

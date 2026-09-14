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
/// <param name="uploadRoot">
/// Thư mục chứa file tải lên từ trình duyệt (<see cref="BackupOptions.UploadRoot"/>).
/// Nằm ngoài mọi danh sách chặn — xem <see cref="IsBlocked"/> để biết vì sao
/// miễn trừ này an toàn. <c>null</c> khi không dùng tính năng tải lên.
/// </param>
public sealed class DirectoryBrowser(
    BackupOptions options,
    string dataDirectory,
    string? uploadRoot = null)
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
                .Select(Normalize)
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

        return [.. defaults.Select(Normalize).OfType<string>()];
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

        var full = Normalize(path);
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
        var full = Normalize(path);
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

    /// <summary>
    /// Chuẩn hoá một đường dẫn về dạng tuyệt đối, đã giải hết <c>..</c> và
    /// <c>.</c>. Trả <c>null</c> nếu đường dẫn không hợp lệ.
    ///
    /// Công khai để <see cref="ResolveUnderRoot"/> và phần nhận file tải lên
    /// dùng **cùng một** cơ chế chuẩn hoá — §5d bắt buộc tái dùng lớp kiểm tra
    /// này, không viết lại.
    /// </summary>
    public static string? Normalize(string path)
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
    public bool IsBlocked(string fullPath)
    {
        var normalized = Normalize(fullPath);
        if (normalized is null) { return true; }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // Thư mục tải lên được miễn trừ, kiểm TRƯỚC danh sách chặn.
        //
        // Vì sao cần: mặc định chặn cả thư mục dữ liệu của hub, nên nếu người
        // vận hành trỏ UploadRoot vào trong đó thì ValidateSource sẽ từ chối
        // chính thư mục hub vừa tạo, và job không lưu được — tính năng chết
        // đúng ở bước biến thư mục đã tải lên thành job.
        //
        // Vì sao an toàn: đây là thư mục do hub tạo ra, chỉ chứa thứ người dùng
        // vừa gửi lên qua endpoint đã xác thực. Miễn trừ đúng nhánh này KHÔNG
        // mở hub.db hay appsettings.Production.json — chúng nằm ngoài nhánh
        // này nên vẫn bị chặn như cũ.
        if (IsUnderUploadRoot(normalized, comparison)) { return false; }

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

    /// <summary>
    /// Ghép một đường dẫn TƯƠNG ĐỐI do client gửi lên vào dưới một thư mục gốc,
    /// và chỉ trả về nếu kết quả thật sự nằm trong gốc đó.
    ///
    /// Khác <see cref="ValidateSource"/>: đây là đường dẫn FILE **chưa tồn tại**
    /// (sắp ghi ra), nên không kiểm tra <c>Directory.Exists</c>. Nhưng dùng đúng
    /// cơ chế chuẩn hoá và cách so tiền tố kèm dấu phân cách — §5d bắt buộc
    /// "cùng một lớp kiểm tra, không viết lại".
    ///
    /// Trình duyệt gửi <c>webkitRelativePath</c> dạng <c>Anh/2026/img.jpg</c>.
    /// Đây chính là chỗ path traversal chui vào, nên mọi thứ dưới đây đều là
    /// phòng thủ có chủ đích, không phải kiểm tra thừa.
    /// </summary>
    public Result<string> ResolveUnderRoot(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Result.Failure<string>(ResultError.Validation("Thiếu đường dẫn tệp."));
        }

        // Đường dẫn tuyệt đối phải chặn NGAY: Path.Combine vứt bỏ gốc khi đối số
        // sau là đường dẫn tuyệt đối, nên Combine(root, "C:\Windows\x") trả về
        // "C:\Windows\x" — file rơi thẳng ra ngoài mà không qua ".." nào.
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
        {
            return Result.Failure<string>(ResultError.Validation("Đường dẫn tệp không hợp lệ."));
        }

        var normalizedRoot = Normalize(root);
        if (normalizedRoot is null)
        {
            return Result.Failure<string>(ResultError.Validation("Thư mục tải lên không hợp lệ."));
        }

        foreach (var segment in relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsSafeSegment(segment))
            {
                return Result.Failure<string>(ResultError.Validation("Tên tệp không hợp lệ."));
            }
        }

        var full = Normalize(Path.Combine(normalizedRoot, relativePath));
        if (full is null)
        {
            return Result.Failure<string>(ResultError.Validation("Đường dẫn tệp không hợp lệ."));
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // So kèm dấu phân cách, cùng lý do với IsBlocked: "D:\HubUploads" không
        // được nuốt "D:\HubUploads-cu".
        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, comparison))
        {
            return Result.Failure<string>(ResultError.Validation(
                "Đường dẫn tệp nằm ngoài thư mục tải lên."));
        }

        // Kiểm cả danh sách chặn: UploadRoot được miễn trừ nên bình thường sẽ
        // qua, nhưng nếu ai đó cấu hình UploadRoot trùng chỗ nhạy cảm thì đây là
        // lưới cuối.
        if (IsBlocked(full))
        {
            return Result.Failure<string>(ResultError.Validation("Đường dẫn tệp bị chặn."));
        }

        return Result.Success(full);
    }

    /// <summary>
    /// Một đoạn tên (thư mục hoặc tệp) có an toàn để ghi ra đĩa không.
    ///
    /// Windows có vài cách làm hai tên khác nhau trỏ về cùng một file, hoặc trỏ
    /// vào thứ không phải file:
    /// <list type="bullet">
    /// <item><c>..</c> — đi ngược lên trên.</item>
    /// <item>Tên thiết bị DOS (<c>CON</c>, <c>NUL</c>, <c>COM1</c>…) — mở ra
    /// thiết bị chứ không tạo file, kể cả khi có phần mở rộng.</item>
    /// <item>Kết thúc bằng dấu chấm hoặc khoảng trắng — Windows lặng lẽ cắt bỏ,
    /// nên "a.txt " và "a.txt" thành cùng một file.</item>
    /// </list>
    /// </summary>
    private static bool IsSafeSegment(string segment)
    {
        if (segment is "." or "..") { return false; }

        if (segment.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { return false; }

        if (segment.EndsWith('.') || segment.EndsWith(' ')) { return false; }

        // Tên thiết bị xét theo phần trước dấu chấm đầu tiên: "NUL.txt" vẫn là
        // thiết bị NUL.
        var stem = segment.Split('.')[0];
        string[] devices =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ];

        return !devices.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Đường dẫn đã chuẩn hoá có nằm trong thư mục tải lên không.</summary>
    private bool IsUnderUploadRoot(string normalized, StringComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(uploadRoot)) { return false; }

        var root = Normalize(uploadRoot);
        if (root is null) { return false; }

        if (normalized.Equals(root, comparison)) { return true; }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return normalized.StartsWith(prefix, comparison);
    }
}

/// <param name="Path">Thư mục đang xem; <c>null</c> khi đang ở danh sách ổ đĩa.</param>
/// <param name="Parent">Thư mục cha; <c>null</c> khi đang ở gốc ổ đĩa.</param>
public sealed record DirectoryListing(
    string? Path,
    string? Parent,
    IReadOnlyList<DirectoryEntry> Entries);

public sealed record DirectoryEntry(string Name, string Path);

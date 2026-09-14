using System.Security.Cryptography;

using Hub.Core.Results;

namespace Hub.Core.Backup;

/// <summary>
/// Nhận file người dùng tải lên từ trình duyệt và ghi vào thư mục tải lên trên
/// máy chủ (§5d).
///
/// **Vì sao tồn tại.** Máy chủ không đọc được đĩa của máy khác, kể cả trong
/// tailnet — giới hạn hệ điều hành, không lách được bằng cấu hình. Nên muốn sao
/// lưu thư mục của một máy khác thì mở web UI tại chính máy đó, trình duyệt đọc
/// thư mục rồi đẩy nội dung lên đây. Sau đó thư mục này thành <c>Source</c> của
/// một job rclone thường — một đường lên cloud duy nhất, không viết đường thứ hai.
///
/// **Vì sao là class thuần chứ không nằm trong endpoint.** <c>Hub.Api.Tests</c>
/// chưa có <c>WebApplicationFactory</c>; nhét logic vào lambda của minimal API
/// là tự làm cho nó không test được.
///
/// Không ghi log tên file hay đường dẫn (§6.5) — chỉ con số và tên thư mục.
/// </summary>
public sealed class UploadReceiver(DirectoryBrowser browser, BackupOptions options)
{
    /// <summary>
    /// Đuôi cho file đang ghi dở.
    ///
    /// Ghi thẳng vào tên thật thì một request đứt giữa chừng để lại file cụt
    /// trông y như file thật, và rclone sẽ đẩy nguyên bản cụt đó lên cloud — hỏng
    /// âm thầm, chỉ lộ ra lúc khôi phục. Job kiểu tải lên vì thế cũng lọc bỏ
    /// <c>*.part</c>.
    /// </summary>
    public const string PartialSuffix = ".part";

    /// <summary>Đệm 80 KB: đủ lớn để không gọi syscall liên tục, đủ nhỏ để không vào LOH.</summary>
    private const int BufferSize = 81920;

    /// <summary>
    /// Thư mục đích của một lần tải lên, đã tạo sẵn trên đĩa.
    ///
    /// Tên thư mục do client gửi nên phải qua đúng lớp kiểm tra như tên file.
    /// </summary>
    public Result<string> PrepareFolder(string folderName)
    {
        var resolved = browser.ResolveUnderRoot(options.UploadRoot, folderName);
        if (resolved.IsFailure)
        {
            return resolved;
        }

        try
        {
            Directory.CreateDirectory(resolved.Value);
            return resolved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<string>(ResultError.Validation(
                "Không tạo được thư mục tải lên trên máy chủ."));
        }
    }

    /// <summary>
    /// Ghi một file vào <paramref name="folder"/>.
    ///
    /// <paramref name="relativePath"/> là <c>webkitRelativePath</c> của trình
    /// duyệt (<c>Anh/2026/img.jpg</c>) đã bỏ đoạn tên thư mục gốc. Đây là dữ
    /// liệu client kiểm soát hoàn toàn nên đi qua
    /// <see cref="DirectoryBrowser.ResolveUnderRoot"/>.
    /// </summary>
    public async Task<UploadFileOutcome> ReceiveFileAsync(
        string folder,
        string relativePath,
        Stream content,
        CancellationToken cancellationToken)
    {
        var resolved = browser.ResolveUnderRoot(folder, relativePath);
        if (resolved.IsFailure)
        {
            return UploadFileOutcome.Rejected;
        }

        var destination = resolved.Value;
        var partial = destination + PartialSuffix;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UploadFileOutcome.Rejected;
        }

        string hash;
        long written;

        try
        {
            (written, hash) = await WritePartialAsync(partial, content, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            return UploadFileOutcome.Rejected;
        }
        catch (OperationCanceledException)
        {
            // Huỷ giữa chừng: xoá phần dở để lần sau tải lại từ đầu, và để
            // không còn .part mồ côi nằm lại.
            TryDelete(partial);
            throw;
        }

        if (written > options.Upload.MaxFileBytes)
        {
            TryDelete(partial);
            return UploadFileOutcome.Rejected;
        }

        // Đã có file cùng đường dẫn và cùng nội dung thì bỏ qua — đây là thứ làm
        // cho việc chọn lại đúng thư mục cũ chỉ tải phần mới, thay vì tải lại tất.
        if (File.Exists(destination) && await IsSameContentAsync(destination, written, hash, cancellationToken))
        {
            TryDelete(partial);
            return UploadFileOutcome.Duplicate;
        }

        try
        {
            File.Move(partial, destination, overwrite: true);
            return UploadFileOutcome.Received;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            return UploadFileOutcome.Rejected;
        }
    }

    /// <summary>
    /// Dọn file <c>.part</c> mồ côi trong thư mục tải lên.
    ///
    /// Gọi lúc khởi động: tab đóng giữa chừng hoặc hub tắt đột ngột để lại file
    /// ghi dở, và không ai dọn thì chúng nằm đó mãi. Cùng tinh thần với
    /// <c>CancelStaleRunsAsync</c>.
    /// </summary>
    public int CleanPartialFiles()
    {
        var root = DirectoryBrowser.Normalize(options.UploadRoot);
        if (root is null || !Directory.Exists(root)) { return 0; }

        var removed = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*" + PartialSuffix, SearchOption.AllDirectories))
            {
                if (TryDelete(file)) { removed++; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dọn dẹp là việc phụ — không chặn khởi động vì nó.
        }

        return removed;
    }

    /// <summary>Ghi ra file tạm, vừa ghi vừa tính hash để khỏi đọc lại lần hai.</summary>
    private static async Task<(long Written, string Hash)> WritePartialAsync(
        string partial,
        Stream content,
        CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long written = 0;

        var stream = new FileStream(
            partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        await using (stream.ConfigureAwait(false))
        {
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
            }
        }

        return (written, Convert.ToHexString(hasher.GetCurrentHash()));
    }

    /// <summary>
    /// File đã có trên đĩa có trùng nội dung với thứ vừa nhận không.
    ///
    /// So kích thước trước vì nó gần như miễn phí; khác cỡ là chắc chắn khác
    /// nội dung, khỏi đọc lại cả file để hash.
    /// </summary>
    private static async Task<bool> IsSameContentAsync(
        string existing,
        long size,
        string hash,
        CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(existing).Length != size) { return false; }

            var stream = new FileStream(
                existing, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

            await using (stream.ConfigureAwait(false))
            {
                var current = await SHA256.HashDataAsync(stream, cancellationToken);
                return Convert.ToHexString(current) == hash;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Không đọc được bản cũ thì coi như khác nội dung và ghi đè — thà
            // tải lại thừa một file còn hơn bỏ sót một file đã đổi.
            return false;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Kết quả nhận một file.</summary>
public enum UploadFileOutcome
{
    /// <summary>Đã ghi mới hoặc ghi đè bản khác nội dung.</summary>
    Received,

    /// <summary>Đã có sẵn file trùng hệt nội dung, bỏ qua.</summary>
    Duplicate,

    /// <summary>Tên không hợp lệ, quá lớn, hoặc không ghi được.</summary>
    Rejected
}

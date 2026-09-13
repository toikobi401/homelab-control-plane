using System.Text.Json;
using System.Text.Json.Serialization;
using Hub.Core.Backup;
using Hub.Core.Results;
using Microsoft.Extensions.Logging;

namespace Hub.Data;

/// <summary>
/// Lưu các công việc sao lưu do người dùng tạo vào <c>backup-jobs.json</c>,
/// đặt cạnh <c>hub.db</c> trong thư mục dữ liệu.
///
/// Vì sao file riêng chứ không ghi vào <c>appsettings.Production.json</c>: xem
/// <see cref="IBackupJobStore"/>. Tóm tắt — file đó giữ token Tailscale và cấu
/// hình MeshCentral, và JSON hỏng cú pháp làm hub bỏ qua toàn bộ nội dung.
/// </summary>
public sealed class JsonBackupJobStore(
    string dataDirectory,
    DirectoryBrowser browser,
    ILogger<JsonBackupJobStore> logger) : IBackupJobStore
{
    private const string FileName = "backup-jobs.json";

    /// <summary>Tên file filter sinh ra, đặt cạnh thư mục nguồn.</summary>
    private const string FilterFileName = ".backupignore";

    /// <summary>
    /// Ghi và đọc không chồng nhau: hai request lưu job cùng lúc có thể làm
    /// file hỏng giữa chừng.
    /// </summary>
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string FilePath => Path.Combine(dataDirectory, FileName);

    public async Task<IReadOnlyList<BackupJobOptions>> GetJobsAsync(
        CancellationToken cancellationToken = default)
    {
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnlockedAsync(cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task<Result> SaveJobAsync(
        BackupJobOptions job,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(job.Name))
        {
            return Result.Failure(ResultError.Validation("Công việc phải có tên."));
        }

        // Tên đi vào URL của endpoint chạy job, và dùng để so khớp — giới hạn
        // ký tự để không phải escape ở mọi nơi dùng tới.
        if (!job.Name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            return Result.Failure(ResultError.Validation(
                "Tên chỉ được dùng chữ, số, dấu gạch ngang và gạch dưới."));
        }

        if (string.IsNullOrWhiteSpace(job.Destination))
        {
            return Result.Failure(ResultError.Validation(
                "Phải khai đích trên cloud, dạng remote:đường/dẫn."));
        }

        // Nguồn phải nằm trong phạm vi cho phép — người dùng có thể gửi thẳng
        // đường dẫn bất kỳ lên API mà không qua bước duyệt thư mục.
        var source = browser.ValidateSource(job.Source);
        if (source.IsFailure)
        {
            return Result.Failure(source.Error!.Value);
        }
        job.Source = source.Value;

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var jobs = (await ReadUnlockedAsync(cancellationToken)).ToList();

            var index = jobs.FindIndex(existing =>
                string.Equals(existing.Name, job.Name, StringComparison.OrdinalIgnoreCase));

            if (index >= 0) { jobs[index] = job; } else { jobs.Add(job); }

            await WriteUnlockedAsync(jobs, cancellationToken);
            return Result.Success();
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task<bool> DeleteJobAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var jobs = (await ReadUnlockedAsync(cancellationToken)).ToList();
            var removed = jobs.RemoveAll(job =>
                string.Equals(job.Name, name, StringComparison.OrdinalIgnoreCase));

            if (removed == 0) { return false; }

            await WriteUnlockedAsync(jobs, cancellationToken);
            return true;
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task<Result<string>> WriteFilterFileAsync(
        string sourceDirectory,
        string content,
        CancellationToken cancellationToken = default)
    {
        // Ghi file vào máy chủ theo yêu cầu từ mạng — phải kiểm tra phạm vi
        // đúng như khi lưu job, không tin đường dẫn người gọi gửi lên.
        var source = browser.ValidateSource(sourceDirectory);
        if (source.IsFailure)
        {
            return Result.Failure<string>(source.Error!.Value);
        }

        var path = Path.Combine(source.Value, FilterFileName);

        try
        {
            // Nội dung ghi nguyên văn, .NET không parse — rclone tự đọc (§2.3).
            await File.WriteAllTextAsync(path, content, cancellationToken);
            return Result.Success(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // §6.5 mục 4: không log đường dẫn.
            logger.LogError(ex, "Không ghi được file filter");
            return Result.Failure<string>(ResultError.Validation(
                "Không ghi được file lọc vào thư mục nguồn."));
        }
    }

    public async Task<string> ReadFilterFileAsync(
        string? filterFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filterFilePath) || !File.Exists(filterFilePath))
        {
            return string.Empty;
        }

        try
        {
            return await File.ReadAllTextAsync(filterFilePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Không đọc được file filter");
            return string.Empty;
        }
    }

    private async Task<IReadOnlyList<BackupJobOptions>> ReadUnlockedAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath)) { return []; }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var jobs = await JsonSerializer.DeserializeAsync<List<BackupJobOptions>>(
                stream, SerializerOptions, cancellationToken);

            return jobs ?? [];
        }
        catch (JsonException ex)
        {
            // File hỏng cú pháp thì mất danh sách job, nhưng hub vẫn chạy —
            // đó chính là lý do tách khỏi appsettings.Production.json.
            logger.LogError(ex, "backup-jobs.json sai cú pháp — bỏ qua các job đã lưu");
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Không đọc được backup-jobs.json");
            return [];
        }
    }

    private async Task WriteUnlockedAsync(
        List<BackupJobOptions> jobs,
        CancellationToken cancellationToken)
    {
        // Ghi ra file tạm rồi đổi tên: mất điện giữa chừng thì file cũ còn
        // nguyên, thay vì thành một file JSON cụt.
        var temporary = FilePath + ".tmp";

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, jobs, SerializerOptions, cancellationToken);
        }

        File.Move(temporary, FilePath, overwrite: true);
    }
}

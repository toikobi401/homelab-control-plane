using System.Diagnostics;
using System.Text.Json;
using Hub.Core.Backup;
using Hub.Core.Results;
using Microsoft.Extensions.Options;

namespace Hub.Api.Backup;

/// <summary>
/// Chạy rclone như một tiến trình con.
///
/// §2.3: "gọi binary rclone" thay vì tự viết client Google Drive. Rclone đã giải
/// những thứ khó của đồng bộ cloud — kiểm tra hash, thử lại khi mạng chập chờn,
/// truyền song song, tiếp tục file dở dang.
/// </summary>
public sealed class RcloneRunner : IBackupRunner
{
    private readonly BackupOptions _options;
    private readonly ILogger<RcloneRunner> _logger;

    public RcloneRunner(IOptions<BackupOptions> options, ILogger<RcloneRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<string>> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (exitCode, output, _) = await RunProcessAsync(
                ["version"], onStats: null, cancellationToken);

            if (exitCode != 0)
            {
                return Result.Failure<string>(ResultError.Validation(
                    "Không chạy được rclone. Kiểm tra Backup:RclonePath."));
            }

            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim() ?? "rclone";

            return Result.Success(firstLine);
        }
        catch (Exception ex)
        {
            // Không tìm thấy file thực thi là lỗi cấu hình, không phải sự cố —
            // nói rõ cho người vận hành thay vì để nó nổ thành 500.
            _logger.LogWarning(ex, "Không gọi được rclone");
            return Result.Failure<string>(ResultError.Validation(
                "Không tìm thấy rclone. Xem docs/backup-setup.md."));
        }
    }

    public async Task<Result<BackupRunStats>> RunAsync(
        BackupJobOptions job,
        CancellationToken cancellationToken)
    {
        // sync xoá file ở đích khi nguồn không còn; copy chỉ thêm. Mặc định là
        // copy vì xoá nhầm ở máy không nên lan lên bản sao lưu.
        var verb = job.DeleteExtra ? "sync" : "copy";

        string[] args =
        [
            verb,
            job.Source,
            job.Destination,

            // Đọc thống kê từ JSON thay vì phân tích text — text đổi theo phiên
            // bản, JSON có cấu trúc ổn định.
            "--use-json-log",
            "--stats-one-line",
            "--stats", "5s",

            // -v để rclone in được khối "stats"; thiếu nó thì không có số liệu
            // nào để đọc.
            "-v",

            // Mạng nhà chập chờn là chuyện thường. Để rclone tự thử lại thay vì
            // báo hỏng cả job vì một file lỗi.
            "--retries", "3",
            "--low-level-retries", "10"
        ];

        BackupRunStats? lastStats = null;

        try
        {
            var (exitCode, _, stderr) = await RunProcessAsync(
                args,
                onStats: stats => lastStats = stats,
                cancellationToken);

            if (exitCode != 0)
            {
                // §6.5 mục 7: chi tiết vào log, người dùng thấy câu chung.
                // stderr có thể chứa đường dẫn file nên KHÔNG đưa ra ngoài.
                _logger.LogError(
                    "rclone thoát với mã {ExitCode} cho job {JobName}",
                    exitCode, job.Name);

                return Result.Failure<BackupRunStats>(ResultError.Validation(
                    $"rclone thất bại (mã {exitCode}). Xem log của hub."));
            }

            // Không có khối stats nào: thường là không có gì để truyền. Đó là
            // thành công, không phải lỗi.
            return Result.Success(lastStats ?? new BackupRunStats(0, 0, 0));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi chạy rclone cho job {JobName}", job.Name);
            return Result.Failure<BackupRunStats>(ResultError.Validation(
                "Không chạy được rclone. Xem log của hub."));
        }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string[] args,
        Action<BackupRunStats>? onStats,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.RclonePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Service chạy dưới LocalSystem không thấy rclone.conf trong hồ sơ
        // người dùng — cùng gốc với vấn đề của cloudflared, xem docs/services.md.
        if (!string.IsNullOrWhiteSpace(_options.ConfigPath))
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(_options.ConfigPath);
        }

        using var process = new Process { StartInfo = startInfo };

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) { stdout.AppendLine(e.Data); }
        };

        // rclone ghi log JSON ra stderr, không phải stdout.
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { return; }
            stderr.AppendLine(e.Data);

            if (onStats is not null && TryParseStats(e.Data, out var stats))
            {
                onStats(stats);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Huỷ mà không giết thì rclone chạy tiếp trong nền, giữ khoá file
            // và tiêu băng thông — người dùng bấm "dừng" nhưng không có gì dừng.
            TryKill(process);
            throw;
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Đọc khối thống kê từ một dòng log JSON của rclone.
    ///
    /// Chỉ lấy con số. Các dòng khác chứa TÊN FILE (<c>"object":"a.txt"</c>) mà
    /// §6.5 mục 4 cấm log — nên hàm này không bao giờ ghi nội dung dòng ra log.
    /// </summary>
    private static bool TryParseStats(string line, out BackupRunStats stats)
    {
        stats = default;

        if (!line.StartsWith('{') || !line.Contains("\"stats\""))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("stats", out var node))
            {
                return false;
            }

            stats = new BackupRunStats(
                FilesTransferred: ReadLong(node, "transfers"),
                BytesTransferred: ReadLong(node, "bytes"),
                Errors: ReadLong(node, "errors"));

            return true;
        }
        catch (JsonException)
        {
            // Dòng không phải JSON hợp lệ — bỏ qua, không phải lỗi đáng báo.
            return false;
        }
    }

    private static long ReadLong(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // entireProcessTree: rclone sinh tiến trình con khi mount.
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Tiến trình đã thoát giữa lúc kiểm tra và lúc giết — không sao.
        }
    }
}

using Hub.Api.Backup;
using Hub.Core.Backup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hub.Api.Tests;

/// <summary>
/// Test chạy rclone thật, không giả lập.
///
/// Vì sao không mock: cái dễ sai nhất ở đây là **định dạng output của rclone** —
/// mock sẽ chỉ kiểm chứng lại giả định của chính mình. Đúng bài học của
/// Hub.Data.Tests: lỗi DateTimeOffset của SQLite chỉ lộ ra khi chạy trên SQLite
/// thật, store trong bộ nhớ che mất nó.
///
/// Máy không có rclone thì test trả về sớm (coi như đạt) thay vì đánh dấu
/// "skipped": xUnit chỉ skip được qua gói Xunit.SkippableFact, mà §9 cấm thêm
/// dependency chỉ để làm đẹp báo cáo test.
/// </summary>
public sealed class RcloneRunnerTests : IDisposable
{
    private readonly string _workDir;
    private readonly bool _hasRclone;

    public RcloneRunnerTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "hub-rclone-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workDir);

        _hasRclone = ProbeRclone();
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { /* dọn dẹp, lỗi không quan trọng */ }
    }

    private static bool ProbeRclone()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "rclone",
                Arguments = "version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) { return false; }
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private RcloneRunner CreateRunner() => new(
        Options.Create(new BackupOptions { RclonePath = "rclone" }),
        NullLogger<RcloneRunner>.Instance);

    [Fact]
    public async Task Probe_tra_ve_phien_ban_khi_co_rclone()
    {
        if (!_hasRclone) { return; }

        var result = await CreateRunner().ProbeAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("rclone", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Copy_dem_dung_so_file_va_so_byte()
    {
        if (!_hasRclone) { return; }

        var source = Path.Combine(_workDir, "src");
        var destination = Path.Combine(_workDir, "dst");
        Directory.CreateDirectory(source);

        // Ba file, mỗi file 1000 byte — con số tròn để so sánh chính xác.
        foreach (var name in new[] { "a.bin", "b.bin", "c.bin" })
        {
            await File.WriteAllBytesAsync(Path.Combine(source, name), new byte[1000]);
        }

        var result = await CreateRunner().RunAsync(
            new BackupJobOptions { Name = "test", Source = source, Destination = destination },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.FilesTransferred);
        Assert.Equal(3000, result.Value.BytesTransferred);
        Assert.Equal(0, result.Value.Errors);
    }

    /// <summary>
    /// rclone VẪN in khối stats khi lỗi (đã kiểm chứng: nguồn không tồn tại →
    /// mã thoát 3 kèm một khối stats rỗng). Chỉ đọc stats mà bỏ qua mã thoát sẽ
    /// báo "thành công, 0 file" — đúng loại lỗi im lặng nguy hiểm nhất với sao lưu.
    /// </summary>
    [Fact]
    public async Task Nguon_khong_ton_tai_thi_that_bai_chu_khong_bao_thanh_cong_rong()
    {
        if (!_hasRclone) { return; }

        var result = await CreateRunner().RunAsync(
            new BackupJobOptions
            {
                Name = "test",
                Source = Path.Combine(_workDir, "khong-ton-tai"),
                Destination = Path.Combine(_workDir, "dst")
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Nguon_rong_thi_thanh_cong_voi_khong_file_nao()
    {
        if (!_hasRclone) { return; }

        var source = Path.Combine(_workDir, "rong");
        Directory.CreateDirectory(source);

        var result = await CreateRunner().RunAsync(
            new BackupJobOptions
            {
                Name = "test",
                Source = source,
                Destination = Path.Combine(_workDir, "dst-rong")
            },
            CancellationToken.None);

        // Không có gì để sao lưu là kết quả hợp lệ, không phải lỗi.
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.FilesTransferred);
    }

    [Fact]
    public async Task Huy_giua_chung_thi_nem_OperationCanceled()
    {
        if (!_hasRclone) { return; }

        var source = Path.Combine(_workDir, "src-lon");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(Path.Combine(source, "f.bin"), new byte[1000]);

        // Huỷ TRƯỚC khi chạy, không đua với rclone.
        //
        // Bản cũ tạo 200 file rồi CancelAfter(200ms) — nhưng chép file cục bộ
        // có thể xong trước hạn đó, và test hỏng ngẫu nhiên (đã gặp: 1 lần hỏng
        // trong 4 lần chạy). Test chập chờn tệ hơn không có test: nó dạy người
        // ta bỏ qua màu đỏ.
        //
        // Điều cần chứng minh không đổi: huỷ phải nổi lên thành
        // OperationCanceledException, không phải lặng lẽ báo thành công.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var running = CreateRunner().RunAsync(
            new BackupJobOptions
            {
                Name = "test",
                Source = source,
                Destination = Path.Combine(_workDir, "dst-lon")
            },
            cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }
}

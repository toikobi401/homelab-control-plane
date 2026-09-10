using Hub.Api.Backup;
using Hub.Core.Backup;
using Microsoft.Extensions.Logging;

namespace Hub.Api.Tests;

/// <summary>
/// Test cho phần kiểm tra cấu hình lúc khởi động.
///
/// Cảnh báo quan trọng nhất là "sao lưu hub.db lên đích không mã hoá": file đó
/// chứa hash mật khẩu và phiên đăng nhập, và người dùng đã chọn mã hoá riêng nó
/// trong khi dữ liệu thường lưu nguyên bản — quyết định dễ bị quên khi thêm job
/// mới về sau.
/// </summary>
public sealed class BackupConfigurationCheckTests : IDisposable
{
    private readonly string _dataDir;
    private readonly RecordingLogger _logger = new();

    public BackupConfigurationCheckTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "hub-cfg-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* dọn dẹp */ }
    }

    private static BackupOptions With(params BackupJobOptions[] jobs) => new() { Jobs = [.. jobs] };

    [Fact]
    public void Sao_luu_thu_muc_du_lieu_len_dich_khong_ma_hoa_thi_bao_loi()
    {
        var options = With(new BackupJobOptions
        {
            Name = "hub-data",
            Source = _dataDir,
            Destination = "gdrive:backup/hub",
            Encrypted = false
        });

        BackupConfigurationCheck.Validate(options, _dataDir, _logger);

        Assert.Contains(_logger.Errors, message =>
            message.Contains("hub-data") && message.Contains("KHÔNG mã hoá"));
    }

    [Fact]
    public void Thu_muc_con_cua_du_lieu_cung_bi_bao()
    {
        var child = Path.Combine(_dataDir, "certs");
        Directory.CreateDirectory(child);

        var options = With(new BackupJobOptions
        {
            Name = "chung-chi",
            Source = child,
            Destination = "gdrive:backup/certs",
            Encrypted = false
        });

        BackupConfigurationCheck.Validate(options, _dataDir, _logger);

        Assert.NotEmpty(_logger.Errors);
    }

    [Fact]
    public void Cung_thu_muc_do_nhung_da_ma_hoa_thi_khong_bao()
    {
        var options = With(new BackupJobOptions
        {
            Name = "hub-data",
            Source = _dataDir,
            Destination = "gdrive-crypt:hub",
            Encrypted = true
        });

        BackupConfigurationCheck.Validate(options, _dataDir, _logger);

        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public void Thu_muc_khac_khong_ma_hoa_thi_khong_bao()
    {
        var other = Path.Combine(Path.GetTempPath(), "hub-cfg-other-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(other);

        try
        {
            var options = With(new BackupJobOptions
            {
                Name = "tai-lieu",
                Source = other,
                Destination = "gdrive:backup/tai-lieu",
                Encrypted = false
            });

            BackupConfigurationCheck.Validate(options, _dataDir, _logger);

            Assert.Empty(_logger.Errors);
        }
        finally
        {
            try { Directory.Delete(other, recursive: true); } catch { /* dọn dẹp */ }
        }
    }

    /// <summary>
    /// Tên trùng thì API chỉ thấy job đầu tiên — job sau bị che hoàn toàn mà
    /// không có dấu hiệu gì.
    /// </summary>
    [Fact]
    public void Ten_job_trung_thi_bao_loi()
    {
        var options = With(
            new BackupJobOptions { Name = "tai-lieu", Source = _dataDir, Destination = "a:x", Encrypted = true },
            new BackupJobOptions { Name = "Tai-Lieu", Source = _dataDir, Destination = "b:y", Encrypted = true });

        BackupConfigurationCheck.Validate(options, _dataDir, _logger);

        Assert.Contains(_logger.Errors, message => message.Contains("trùng"));
    }

    [Fact]
    public void Thieu_Source_hoac_Destination_thi_bao_loi()
    {
        var options = With(new BackupJobOptions
        {
            Name = "hong",
            Source = "",
            Destination = "gdrive:x"
        });

        BackupConfigurationCheck.Validate(options, _dataDir, _logger);

        Assert.Contains(_logger.Errors, message => message.Contains("hong"));
    }

    [Fact]
    public void Nguon_khong_ton_tai_thi_chi_canh_bao_chu_khong_phai_loi()
    {
        var options = With(new BackupJobOptions
        {
            Name = "o-ngoai",
            Source = Path.Combine(_dataDir, "khong-co"),
            Destination = "gdrive:x",
            Encrypted = true
        });

        BackupConfigurationCheck.Validate(options, _dataDir, _logger);

        // Ổ ngoài chưa cắm là chuyện bình thường — cảnh báo, không phải lỗi.
        Assert.Empty(_logger.Errors);
        Assert.NotEmpty(_logger.Warnings);
    }

    [Fact]
    public void Job_bi_tat_thi_khong_kiem_tra_nguon()
    {
        var options = With(new BackupJobOptions
        {
            Name = "tat",
            Source = _dataDir,
            Destination = "gdrive:x",
            Encrypted = false,
            Enabled = false
        });

        BackupConfigurationCheck.Validate(options, _dataDir, _logger);

        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public void Khong_co_job_nao_thi_khong_bao_gi()
    {
        BackupConfigurationCheck.Validate(new BackupOptions(), _dataDir, _logger);

        Assert.Empty(_logger.Errors);
        Assert.Empty(_logger.Warnings);
    }
}

/// <summary>Logger ghi lại thông báo để test kiểm tra.</summary>
internal sealed class RecordingLogger : ILogger
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        if (logLevel >= LogLevel.Error) { Errors.Add(message); }
        else if (logLevel == LogLevel.Warning) { Warnings.Add(message); }
    }
}

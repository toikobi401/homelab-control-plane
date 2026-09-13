using Hub.Core.Backup;
using Hub.Core.Results;

namespace Hub.Core.Tests;

/// <summary>
/// Test cho năng lực 3. Mỗi test nhắm vào một quy tắc cụ thể, không test lấy lệ.
/// </summary>
public sealed class BackupServiceTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly InMemoryBackupStore _store = new();
    private readonly BackupJobLocks _locks = new();

    private static BackupOptions OptionsWith(params BackupJobOptions[] jobs) =>
        new() { Jobs = [.. jobs] };

    private static BackupJobOptions Job(string name = "tai-lieu") => new()
    {
        Name = name,
        Source = @"D:\Tai lieu",
        Destination = "gdrive:backup/tai-lieu"
    };

    private readonly InMemoryBackupJobStore _jobStore = new();

    private BackupService CreateService(FakeBackupRunner runner, BackupOptions options) =>
        new(runner, _store, _clock, options, _locks, _jobStore);

    [Fact]
    public async Task Job_khong_ton_tai_thi_bao_loi_validation()
    {
        var service = CreateService(new FakeBackupRunner(), OptionsWith(Job()));

        var result = await service.RunJobAsync("khong-co");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }

    [Fact]
    public async Task Job_bi_tat_thi_khong_chay_duoc()
    {
        var job = Job();
        job.Enabled = false;
        var service = CreateService(new FakeBackupRunner(), OptionsWith(job));

        var result = await service.RunJobAsync(job.Name);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Chay_thanh_cong_thi_ghi_thong_ke_vao_lich_su()
    {
        var runner = new FakeBackupRunner { Stats = new BackupRunStats(12, 3456, 0) };
        var service = CreateService(runner, OptionsWith(Job()));

        var result = await service.RunJobAsync("tai-lieu");

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupRunStatus.Succeeded, result.Value.Status);
        Assert.Equal(12, result.Value.FilesTransferred);
        Assert.Equal(3456, result.Value.BytesTransferred);
        Assert.NotNull(result.Value.FinishedAt);

        var history = await _store.GetRunsAsync(10);
        Assert.Single(history);
    }

    /// <summary>
    /// rclone trả mã 0 mà vẫn báo lỗi lẻ tẻ (file bị khoá, chẳng hạn). Coi là
    /// thất bại: "sao lưu xong" mà thiếu file là thông tin sai lệch nguy hiểm
    /// hơn cả báo lỗi thẳng.
    /// </summary>
    [Fact]
    public async Task Rclone_bao_loi_le_te_thi_van_tinh_la_that_bai()
    {
        var runner = new FakeBackupRunner { Stats = new BackupRunStats(10, 100, 2) };
        var service = CreateService(runner, OptionsWith(Job()));

        var result = await service.RunJobAsync("tai-lieu");

        Assert.True(result.IsSuccess); // gọi được, nhưng kết quả là Failed
        Assert.Equal(BackupRunStatus.Failed, result.Value.Status);
        Assert.Equal(2, result.Value.Errors);
        Assert.NotNull(result.Value.ErrorMessage);
    }

    [Fact]
    public async Task Runner_that_bai_thi_ghi_lai_la_Failed()
    {
        var runner = new FakeBackupRunner
        {
            Failure = ResultError.Validation("rclone thất bại (mã 1).")
        };
        var service = CreateService(runner, OptionsWith(Job()));

        var result = await service.RunJobAsync("tai-lieu");

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupRunStatus.Failed, result.Value.Status);

        // §6.5 mục 7: thông báo cho người dùng, không phải stack trace.
        Assert.Contains("rclone", result.Value.ErrorMessage!);
    }

    /// <summary>
    /// Bấm hai lần không được tạo hai lần sao lưu lên cùng một đích — chúng sẽ
    /// ghi đè lẫn nhau và thống kê trả về vô nghĩa.
    /// </summary>
    [Fact]
    public async Task Job_dang_chay_thi_lan_goi_thu_hai_bi_tu_choi()
    {
        var gate = new TaskCompletionSource();
        var runner = new FakeBackupRunner { BlockUntil = gate.Task };
        var options = OptionsWith(Job());

        // Hai service khác nhau, giống hai request khác nhau — khoá phải dùng
        // chung (BackupJobLocks là singleton), không nằm trong từng instance.
        var first = CreateService(runner, options);
        var second = CreateService(new FakeBackupRunner(), options);

        var running = first.RunJobAsync("tai-lieu");
        await runner.Started.Task;

        var rejected = await second.RunJobAsync("tai-lieu");

        Assert.True(rejected.IsFailure);
        Assert.Equal("conflict", rejected.Error!.Value.Code);

        gate.SetResult();
        var completed = await running;
        Assert.Equal(BackupRunStatus.Succeeded, completed.Value.Status);
    }

    [Fact]
    public async Task Chay_xong_thi_khoa_duoc_nha_ra()
    {
        var options = OptionsWith(Job());
        var service = CreateService(new FakeBackupRunner(), options);

        await service.RunJobAsync("tai-lieu");

        Assert.False(service.IsRunning("tai-lieu"));

        var second = await service.RunJobAsync("tai-lieu");
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task Ten_job_khong_phan_biet_hoa_thuong()
    {
        var service = CreateService(new FakeBackupRunner(), OptionsWith(Job("Tai-Lieu")));

        var result = await service.RunJobAsync("tai-lieu");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Lich_su_gioi_han_theo_HistoryLimit()
    {
        var options = OptionsWith(Job());
        options.HistoryLimit = 3;
        var service = CreateService(new FakeBackupRunner(), options);

        for (var i = 0; i < 5; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            await service.RunJobAsync("tai-lieu");
        }

        var history = await service.GetHistoryAsync(100);
        Assert.Equal(3, history.Count);
    }
}

internal sealed class FakeBackupRunner : IBackupRunner
{
    public BackupRunStats Stats { get; set; } = new(0, 0, 0);

    public ResultError? Failure { get; set; }

    /// <summary>Chặn tới khi task này xong — để test khoá đồng thời.</summary>
    public Task? BlockUntil { get; set; }

    /// <summary>Báo hiệu runner đã bắt đầu, để test không phải sleep.</summary>
    public TaskCompletionSource Started { get; } = new();

    public async Task<Result<BackupRunStats>> RunAsync(
        BackupJobOptions job,
        CancellationToken cancellationToken)
    {
        Started.TrySetResult();

        if (BlockUntil is not null)
        {
            await BlockUntil.WaitAsync(cancellationToken);
        }

        return Failure is not null
            ? Result.Failure<BackupRunStats>(Failure.Value)
            : Result.Success(Stats);
    }

    public Task<Result<string>> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success("rclone v1.75.1"));
}

internal sealed class InMemoryBackupStore : IBackupStore
{
    private readonly List<BackupRun> _runs = [];
    private int _nextId = 1;

    public Task<BackupRun> StartRunAsync(BackupRun run, CancellationToken cancellationToken = default)
    {
        run.Id = _nextId++;
        _runs.Add(run);
        return Task.FromResult(run);
    }

    public Task FinishRunAsync(BackupRun run, CancellationToken cancellationToken = default)
    {
        var existing = _runs.FirstOrDefault(r => r.Id == run.Id);
        if (existing is not null)
        {
            existing.Status = run.Status;
            existing.FinishedAt = run.FinishedAt;
            existing.FilesTransferred = run.FilesTransferred;
            existing.BytesTransferred = run.BytesTransferred;
            existing.Errors = run.Errors;
            existing.ErrorMessage = run.ErrorMessage;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BackupRun>> GetRunsAsync(int limit, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BackupRun> result = _runs
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<BackupRun?> GetLatestRunAsync(string jobName, CancellationToken cancellationToken = default)
    {
        var result = _runs
            .Where(r => r.JobName == jobName)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .FirstOrDefault();
        return Task.FromResult(result);
    }

    public Task<int> CancelStaleRunsAsync(CancellationToken cancellationToken = default)
    {
        var stale = _runs.Where(r => r.Status == BackupRunStatus.Running).ToList();
        foreach (var run in stale)
        {
            run.Status = BackupRunStatus.Cancelled;
            run.ErrorMessage = "Hub đã tắt khi đang chạy.";
        }
        return Task.FromResult(stale.Count);
    }

    public Task<int> TrimHistoryAsync(int keep, CancellationToken cancellationToken = default)
    {
        var toRemove = _runs
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .Skip(keep)
            .Where(r => r.Status != BackupRunStatus.Running)
            .ToList();

        foreach (var run in toRemove) { _runs.Remove(run); }
        return Task.FromResult(toRemove.Count);
    }
}

/// <summary>
/// Store job trong bo nho. Mac dinh rong — moi test hien co dung job khai trong
/// BackupOptions, nen gop hai nguon khong duoc lam doi hanh vi cu.
/// </summary>
internal sealed class InMemoryBackupJobStore : IBackupJobStore
{
    private readonly List<BackupJobOptions> _jobs = [];

    public Task<IReadOnlyList<BackupJobOptions>> GetJobsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BackupJobOptions>>(_jobs);

    public Task<Result> SaveJobAsync(BackupJobOptions job, CancellationToken cancellationToken = default)
    {
        _jobs.RemoveAll(j => string.Equals(j.Name, job.Name, StringComparison.OrdinalIgnoreCase));
        _jobs.Add(job);
        return Task.FromResult(Result.Success());
    }

    public Task<bool> DeleteJobAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.RemoveAll(j =>
            string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase)) > 0);

    public Task<Result<string>> WriteFilterFileAsync(
        string sourceDirectory, string content, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(System.IO.Path.Combine(sourceDirectory, ".backupignore")));

    public Task<string> ReadFilterFileAsync(
        string? filterFilePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);
}

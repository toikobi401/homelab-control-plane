using Hub.Core.Abstractions;
using Hub.Core.Results;

namespace Hub.Core.Backup;

/// <summary>
/// Điều phối việc sao lưu — năng lực 3.
///
/// Việc nặng giao cho rclone (§2.3). Phần này lo: một job chỉ chạy một lần tại
/// một thời điểm, ghi lịch sử, và không để bản ghi kẹt ở trạng thái "đang chạy".
/// </summary>
public sealed class BackupService
{
    private readonly IBackupRunner _runner;
    private readonly IBackupStore _store;
    private readonly IClock _clock;
    private readonly BackupOptions _options;

    // Khoá theo tên job: chạy hai lần sync cùng một đích sẽ khiến chúng ghi đè
    // lẫn nhau, và thống kê trả về vô nghĩa. Bộ khoá là singleton (xem
    // BackupJobLocks) — service này scoped nên không giữ khoá trong chính nó.
    private readonly BackupJobLocks _locks;
    private readonly IBackupJobStore _jobStore;

    public BackupService(
        IBackupRunner runner,
        IBackupStore store,
        IClock clock,
        BackupOptions options,
        BackupJobLocks locks,
        IBackupJobStore jobStore)
    {
        _runner = runner;
        _store = store;
        _clock = clock;
        _options = options;
        _locks = locks;
        _jobStore = jobStore;
    }

    /// <summary>
    /// Các công việc đang bật, gộp từ hai nguồn: khai tay trong
    /// <c>appsettings</c> và tạo từ giao diện (<see cref="IBackupJobStore"/>).
    ///
    /// Job khai tay thắng khi trùng tên — cấu hình của người vận hành có thẩm
    /// quyền cao hơn thứ tạo qua API, và <see cref="IBackupJobStore"/> đã từ
    /// chối lưu tên trùng nên trường hợp này chỉ xảy ra khi ai đó sửa file
    /// <c>appsettings</c> sau.
    /// </summary>
    public async Task<IReadOnlyList<BackupJobOptions>> GetJobsAsync(
        CancellationToken cancellationToken = default)
    {
        var fromConfig = _options.Jobs.Where(j => j.Enabled).ToList();
        var stored = await _jobStore.GetJobsAsync(cancellationToken);

        var names = new HashSet<string>(
            fromConfig.Select(j => j.Name), StringComparer.OrdinalIgnoreCase);

        fromConfig.AddRange(stored.Where(j => j.Enabled && names.Add(j.Name)));
        return fromConfig;
    }

    public async Task<BackupJobOptions?> FindJobAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var jobs = await GetJobsAsync(cancellationToken);
        return jobs.FirstOrDefault(j =>
            string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Chạy một job. Đang chạy rồi thì từ chối thay vì xếp hàng — người dùng
    /// bấm hai lần không nên tạo ra hai lần sao lưu.
    /// </summary>
    public async Task<Result<BackupRun>> RunJobAsync(
        string jobName,
        CancellationToken cancellationToken = default)
    {
        var job = await FindJobAsync(jobName, cancellationToken);
        if (job is null)
        {
            return Result.Failure<BackupRun>(
                ResultError.Validation("Không có công việc sao lưu nào tên như vậy."));
        }

        var gate = _locks.Get(job.Name);

        // Không chờ: đang chạy thì báo ngay. Xếp hàng sẽ khiến người dùng bấm
        // ba lần rồi ngồi đợi ba lần sao lưu liên tiếp mà không hiểu vì sao.
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            return Result.Failure<BackupRun>(
                ResultError.Conflict("Công việc này đang chạy."));
        }

        try
        {
            return await ExecuteAsync(job, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Result<BackupRun>> ExecuteAsync(
        BackupJobOptions job,
        CancellationToken cancellationToken)
    {
        var run = await _store.StartRunAsync(
            new BackupRun
            {
                JobName = job.Name,
                Status = BackupRunStatus.Running,
                StartedAt = _clock.UtcNow
            },
            cancellationToken);

        // Giới hạn thời gian: một lần sync treo sẽ giữ khoá vĩnh viễn và chặn
        // mọi lần chạy sau, mà giao diện chỉ hiện "đang chạy" mãi.
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(_options.TimeoutMinutes));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);

        Result<BackupRunStats> result;
        try
        {
            result = await _runner.RunAsync(job, linked.Token);
        }
        catch (OperationCanceledException)
        {
            run.Status = BackupRunStatus.Cancelled;
            run.ErrorMessage = timeout.IsCancellationRequested
                ? $"Quá {_options.TimeoutMinutes} phút, đã dừng."
                : "Đã bị huỷ.";
            run.FinishedAt = _clock.UtcNow;

            await _store.FinishRunAsync(run, CancellationToken.None);
            return Result.Success(run);
        }

        run.FinishedAt = _clock.UtcNow;

        if (result.IsFailure)
        {
            run.Status = BackupRunStatus.Failed;
            run.ErrorMessage = result.Error!.Value.Message;
        }
        else
        {
            var stats = result.Value;
            run.FilesTransferred = stats.FilesTransferred;
            run.BytesTransferred = stats.BytesTransferred;
            run.Errors = stats.Errors;

            // rclone có thể trả mã 0 mà vẫn có lỗi lẻ tẻ (một vài file bị khoá,
            // chẳng hạn). Coi đó là thất bại: "sao lưu xong" mà thiếu file là
            // thông tin sai lệch nguy hiểm hơn cả báo lỗi.
            run.Status = stats.Errors > 0
                ? BackupRunStatus.Failed
                : BackupRunStatus.Succeeded;

            if (stats.Errors > 0)
            {
                run.ErrorMessage = $"rclone báo {stats.Errors} lỗi — xem log của rclone.";
            }
        }

        // CancellationToken.None: đã chạy xong rồi, phải ghi được kết quả kể cả
        // khi request bị huỷ. Không ghi thì bản ghi kẹt ở Running.
        await _store.FinishRunAsync(run, CancellationToken.None);
        await _store.TrimHistoryAsync(_options.HistoryLimit, CancellationToken.None);

        return Result.Success(run);
    }

    public Task<IReadOnlyList<BackupRun>> GetHistoryAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        _store.GetRunsAsync(Math.Clamp(limit, 1, _options.HistoryLimit), cancellationToken);

    public Task<BackupRun?> GetLatestRunAsync(
        string jobName,
        CancellationToken cancellationToken = default) =>
        _store.GetLatestRunAsync(jobName, cancellationToken);

    public Task<Result<string>> ProbeRcloneAsync(CancellationToken cancellationToken = default) =>
        _runner.ProbeAsync(cancellationToken);

    /// <summary>Job này có đang chạy không.</summary>
    public bool IsRunning(string jobName) => _locks.IsHeld(jobName);
}

using Hub.Core.Backup;
using Microsoft.EntityFrameworkCore;

namespace Hub.Data;

/// <summary>
/// Hiện thực <see cref="IBackupStore"/> bằng EF Core. Toàn bộ kiến thức về EF
/// dừng lại ở đây — Hub.Core không biết gì (§3, luật phụ thuộc).
/// </summary>
public sealed class EfBackupStore(HubDbContext dbContext) : IBackupStore
{
    public async Task<BackupRun> StartRunAsync(
        BackupRun run,
        CancellationToken cancellationToken = default)
    {
        dbContext.BackupRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task FinishRunAsync(
        BackupRun run,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.BackupRuns
            .FirstOrDefaultAsync(row => row.Id == run.Id, cancellationToken);

        if (existing is null) { return; }

        existing.Status = run.Status;
        existing.FinishedAt = run.FinishedAt;
        existing.FilesTransferred = run.FilesTransferred;
        existing.BytesTransferred = run.BytesTransferred;
        existing.Errors = run.Errors;
        existing.ErrorMessage = run.ErrorMessage;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BackupRun>> GetRunsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.BackupRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<BackupRun?> GetLatestRunAsync(
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.BackupRuns
            .AsNoTracking()
            .Where(run => run.JobName == jobName)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CancelStaleRunsAsync(CancellationToken cancellationToken = default)
    {
        // Máy tắt giữa lúc sao lưu thì bản ghi kẹt ở Running mãi mãi, và giao
        // diện hiện "đang chạy" cho một tiến trình đã chết từ lâu.
        return await dbContext.BackupRuns
            .Where(run => run.Status == BackupRunStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, BackupRunStatus.Cancelled)
                    .SetProperty(run => run.ErrorMessage, "Hub đã tắt khi đang chạy."),
                cancellationToken);
    }

    public async Task<int> TrimHistoryAsync(
        int keep,
        CancellationToken cancellationToken = default)
    {
        // Lấy mốc thời gian của bản ghi thứ `keep`, rồi xoá mọi thứ cũ hơn.
        // Cách này chỉ cần hai truy vấn, không phải tải toàn bộ Id về bộ nhớ.
        var cutoff = await dbContext.BackupRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Skip(keep)
            .Select(run => (DateTimeOffset?)run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (cutoff is null) { return 0; }

        return await dbContext.BackupRuns
            .Where(run => run.StartedAt <= cutoff.Value && run.Status != BackupRunStatus.Running)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

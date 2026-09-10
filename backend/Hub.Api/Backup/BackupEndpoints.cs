using Hub.Api.Security;
using Hub.Core.Backup;
using Hub.Core.Results;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Hub.Api.Backup;

/// <summary>
/// Endpoint năng lực 3 — sao lưu lên cloud qua rclone.
///
/// Mọi endpoint yêu cầu đăng nhập. Endpoint chạy sao lưu còn cần antiforgery
/// token (§6.5 mục 5): nó thay đổi trạng thái và tiêu băng thông, nên không
/// được để một trang web khác kích hoạt thay người dùng.
/// </summary>
public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/backup")
            .WithTags("Backup")
            .RequireAuthorization();

        group.MapGet("/", GetStatusAsync)
            .WithName("GetBackupStatus")
            .WithSummary("Danh sách công việc sao lưu kèm trạng thái lần chạy gần nhất");

        group.MapGet("/history", GetHistoryAsync)
            .WithName("GetBackupHistory")
            .WithSummary("Lịch sử các lần sao lưu");

        group.MapPost("/{jobName}/run", RunJobAsync)
            .RequireAntiforgery()
            .WithName("RunBackupJob")
            .WithSummary("Chạy một công việc sao lưu");

        return builder;
    }

    private static async Task<Ok<BackupStatusDto>> GetStatusAsync(
        BackupService backupService,
        CancellationToken cancellationToken)
    {
        var jobs = backupService.GetJobs();

        // Kiểm tra rclone ngay ở màn hình trạng thái: thiếu nó là lỗi cấu hình
        // của người vận hành, phải nói rõ trước khi họ bấm "chạy" rồi mới thấy
        // hỏng.
        var probe = await backupService.ProbeRcloneAsync(cancellationToken);

        var items = new List<BackupJobDto>(jobs.Count);
        foreach (var job in jobs)
        {
            var latest = await backupService.GetLatestRunAsync(job.Name, cancellationToken);

            items.Add(new BackupJobDto(
                Name: job.Name,
                Encrypted: job.Encrypted,
                DeleteExtra: job.DeleteExtra,
                IsRunning: backupService.IsRunning(job.Name),
                LatestRun: latest is null ? null : ToDto(latest)));
        }

        return TypedResults.Ok(new BackupStatusDto(
            RcloneAvailable: probe.IsSuccess,
            RcloneVersion: probe.IsSuccess ? probe.Value : null,
            Jobs: items));
    }

    private static async Task<Ok<IReadOnlyList<BackupRunDto>>> GetHistoryAsync(
        BackupService backupService,
        CancellationToken cancellationToken,
        int limit = 20)
    {
        var runs = await backupService.GetHistoryAsync(limit, cancellationToken);
        IReadOnlyList<BackupRunDto> items = runs.Select(ToDto).ToList();
        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok<BackupRunDto>, ProblemHttpResult>> RunJobAsync(
        string jobName,
        BackupService backupService,
        CancellationToken cancellationToken)
    {
        var result = await backupService.RunJobAsync(jobName, cancellationToken);

        if (result.IsFailure)
        {
            return ToProblem(result.Error!.Value);
        }

        return TypedResults.Ok(ToDto(result.Value));
    }

    private static BackupRunDto ToDto(BackupRun run) => new(
        Id: run.Id,
        JobName: run.JobName,
        Status: run.Status.ToString(),
        StartedAt: run.StartedAt,
        FinishedAt: run.FinishedAt,
        FilesTransferred: run.FilesTransferred,
        BytesTransferred: run.BytesTransferred,
        Errors: run.Errors,
        ErrorMessage: run.ErrorMessage);

    private static ProblemHttpResult ToProblem(ResultError error)
    {
        // §6.5 mục 7: người dùng chỉ thấy thông báo chung, chi tiết vào log.
        var statusCode = error.Code switch
        {
            "validation" => StatusCodes.Status400BadRequest,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return TypedResults.Problem(detail: error.Message, statusCode: statusCode);
    }
}

/// <param name="RcloneAvailable">Gọi được rclone không — thiếu là lỗi cấu hình.</param>
/// <param name="Jobs">Các công việc đang bật.</param>
public sealed record BackupStatusDto(
    bool RcloneAvailable,
    string? RcloneVersion,
    IReadOnlyList<BackupJobDto> Jobs);

/// <param name="Encrypted">Đích có mã hoá không — hiện lên giao diện để thấy rõ file nào được bảo vệ.</param>
/// <param name="DeleteExtra">Có xoá file thừa ở đích không (sync) hay chỉ thêm (copy).</param>
public sealed record BackupJobDto(
    string Name,
    bool Encrypted,
    bool DeleteExtra,
    bool IsRunning,
    BackupRunDto? LatestRun);

public sealed record BackupRunDto(
    int Id,
    string JobName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    long FilesTransferred,
    long BytesTransferred,
    long Errors,
    string? ErrorMessage);

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

        group.MapGet("/browse", BrowseAsync)
            .WithName("BrowseBackupDirectories")
            .WithSummary("Duyệt thư mục trên máy chạy hub để chọn nguồn sao lưu");

        group.MapGet("/remotes", GetRemotesAsync)
            .WithName("GetRcloneRemotes")
            .WithSummary("Danh sách remote đã khai trong rclone.conf");

        group.MapGet("/presets", GetPresets)
            .WithName("GetBackupFilterPresets")
            .WithSummary("Mẫu nội dung file lọc");

        group.MapPost("/jobs", SaveJobAsync)
            .RequireAntiforgery()
            .WithName("SaveBackupJob")
            .WithSummary("Tạo hoặc sửa một công việc sao lưu");

        group.MapDelete("/jobs/{jobName}", DeleteJobAsync)
            .RequireAntiforgery()
            .WithName("DeleteBackupJob")
            .WithSummary("Xoá một công việc sao lưu do người dùng tạo");

        group.MapGet("/jobs/{jobName}/filter", GetFilterAsync)
            .WithName("GetBackupJobFilter")
            .WithSummary("Nội dung file lọc của một công việc");

        return builder;
    }

    private static async Task<Ok<BackupStatusDto>> GetStatusAsync(
        BackupService backupService,
        IBackupJobStore jobStore,
        CancellationToken cancellationToken)
    {
        var jobs = await backupService.GetJobsAsync(cancellationToken);

        // Job nào nằm trong store thì sửa/xoá được; job khai tay trong
        // appsettings thì không. Giao diện dựa vào cờ này để ẩn nút xoá thay vì
        // hiện một nút mà bấm vào chắc chắn nhận 404.
        var stored = await jobStore.GetJobsAsync(cancellationToken);
        var editable = new HashSet<string>(
            stored.Select(j => j.Name), StringComparer.OrdinalIgnoreCase);

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
                IsEditable: editable.Contains(job.Name),
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

    /// <summary>
    /// Duyệt thư mục để chọn nguồn sao lưu.
    ///
    /// Chỉ trả tên thư mục, không đọc nội dung file. Phạm vi giới hạn bởi
    /// <c>Backup:BlockedPaths</c> — xem <see cref="DirectoryBrowser"/> để biết
    /// vì sao vẫn cần chặn vài thư mục dù đã mở duyệt toàn ổ đĩa.
    /// </summary>
    private static Results<Ok<DirectoryListingDto>, ProblemHttpResult> BrowseAsync(
        DirectoryBrowser browser,
        string? path = null)
    {
        var result = browser.List(path);

        if (result.IsFailure)
        {
            return ToProblem(result.Error!.Value);
        }

        var listing = result.Value;
        return TypedResults.Ok(new DirectoryListingDto(
            Path: listing.Path,
            Parent: listing.Parent,
            Entries: [.. listing.Entries.Select(e => new DirectoryEntryDto(e.Name, e.Path))]));
    }

    /// <summary>
    /// Remote của rclone, để giao diện cho chọn thay vì gõ tay.
    ///
    /// Tên remote phân biệt hoa thường; gõ nhầm thì job hỏng lúc CHẠY chứ không
    /// phải lúc lưu, và người dùng chỉ thấy "rclone thất bại (mã 1)".
    /// </summary>
    private static async Task<Ok<IReadOnlyList<RemoteDto>>> GetRemotesAsync(
        BackupService backupService,
        CancellationToken cancellationToken)
    {
        var result = await backupService.ListRemotesAsync(cancellationToken);

        // Không đọc được thì trả rỗng, không phải lỗi: giao diện rơi về ô gõ tự
        // do, vẫn dùng được.
        IReadOnlyList<RemoteDto> remotes = result.IsSuccess
            ? [.. result.Value.Select(r => new RemoteDto(r.Name, r.Type))]
            : [];

        return TypedResults.Ok(remotes);
    }

    private static Ok<IReadOnlyList<FilterPresetDto>> GetPresets()
    {
        IReadOnlyList<FilterPresetDto> presets =
            [.. FilterPresets.All.Select(p => new FilterPresetDto(p.Name, p.Description, p.Content))];

        return TypedResults.Ok(presets);
    }

    /// <summary>
    /// Tạo hoặc sửa một công việc, kèm nội dung file lọc.
    ///
    /// Ghi vào <c>backup-jobs.json</c> riêng, KHÔNG vào
    /// <c>appsettings.Production.json</c> — file đó giữ token Tailscale và cấu
    /// hình MeshCentral, một lỗi ghi sẽ làm hỏng toàn bộ cấu hình hub.
    /// </summary>
    private static async Task<Results<Ok<SaveJobResultDto>, ProblemHttpResult>> SaveJobAsync(
        SaveJobRequest request,
        IBackupJobStore jobStore,
        BackupService backupService,
        CancellationToken cancellationToken)
    {
        // Trùng tên với job khai tay trong appsettings thì từ chối: job khai tay
        // luôn thắng khi gộp, nên job lưu ở đây sẽ không bao giờ chạy — im lặng
        // ghi vào là lừa người dùng.
        var existing = await backupService.FindJobAsync(request.Name, cancellationToken);
        var stored = await jobStore.GetJobsAsync(cancellationToken);
        var isStored = stored.Any(j =>
            string.Equals(j.Name, request.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null && !isStored)
        {
            return ToProblem(ResultError.Conflict(
                "Đã có công việc cùng tên khai trong file cấu hình. Đổi tên khác."));
        }

        // Kiem tra remote co that truoc khi luu — de den luc chay moi hong thi
        // nguoi dung chi thay "rclone that bai (ma 1)".
        var destinationCheck = await backupService.ValidateDestinationAsync(
            request.Destination.Trim(), cancellationToken);

        if (destinationCheck.IsFailure)
        {
            return ToProblem(destinationCheck.Error!.Value);
        }

        var job = new BackupJobOptions
        {
            Name = request.Name.Trim(),
            Source = request.Source,
            Destination = request.Destination.Trim(),
            Encrypted = request.Encrypted,
            DeleteExtra = request.DeleteExtra,
            Enabled = true
        };

        // Ghi file lọc trước khi lưu job: có nội dung thì job mới cần trỏ tới.
        if (!string.IsNullOrWhiteSpace(request.FilterContent))
        {
            var filter = await jobStore.WriteFilterFileAsync(
                request.Source, request.FilterContent, cancellationToken);

            if (filter.IsFailure)
            {
                return ToProblem(filter.Error!.Value);
            }

            job.FilterFile = filter.Value;
        }

        var saved = await jobStore.SaveJobAsync(job, cancellationToken);
        if (saved.IsFailure)
        {
            return ToProblem(saved.Error!.Value);
        }

        return TypedResults.Ok(new SaveJobResultDto(job.Name, job.FilterFile));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteJobAsync(
        string jobName,
        IBackupJobStore jobStore,
        CancellationToken cancellationToken)
    {
        var removed = await jobStore.DeleteJobAsync(jobName, cancellationToken);

        // Không xoá file .backupignore: nó nằm trong thư mục của người dùng,
        // xoá file trong thư mục họ là việc vượt quá thứ họ vừa yêu cầu.
        return removed ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<FilterContentDto>, NotFound>> GetFilterAsync(
        string jobName,
        BackupService backupService,
        IBackupJobStore jobStore,
        CancellationToken cancellationToken)
    {
        var job = await backupService.FindJobAsync(jobName, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var content = await jobStore.ReadFilterFileAsync(job.FilterFile, cancellationToken);
        return TypedResults.Ok(new FilterContentDto(job.Source, job.FilterFile, content));
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
/// <param name="IsEditable">
/// Sửa/xoá được qua API không. Job khai tay trong <c>appsettings</c> thì
/// <c>false</c> — giao diện phải ẩn nút xoá thay vì hiện một nút mà bấm vào
/// chắc chắn nhận 404.
/// </param>
public sealed record BackupJobDto(
    string Name,
    bool Encrypted,
    bool DeleteExtra,
    bool IsRunning,
    bool IsEditable,
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

/// <param name="Path">Thư mục đang xem; <c>null</c> nghĩa là danh sách ổ gốc.</param>
/// <param name="Parent">Thư mục cha, <c>null</c> khi đã ở gốc cho phép.</param>
public sealed record DirectoryListingDto(
    string? Path,
    string? Parent,
    IReadOnlyList<DirectoryEntryDto> Entries);

public sealed record DirectoryEntryDto(string Name, string Path);

/// <param name="Name">Tên remote, KHÔNG kèm dấu hai chấm.</param>
/// <param name="Type">Loại remote: <c>drive</c>, <c>crypt</c>, …</param>
public sealed record RemoteDto(string Name, string Type);

/// <param name="Content">Nội dung file lọc, ghi nguyên văn — .NET không parse.</param>
public sealed record FilterPresetDto(string Name, string Description, string Content);

/// <param name="FilterContent">
/// Nội dung file lọc. Để trống thì không sinh file, sao lưu toàn bộ thư mục.
/// </param>
public sealed record SaveJobRequest(
    string Name,
    string Source,
    string Destination,
    bool Encrypted,
    bool DeleteExtra,
    string? FilterContent);

/// <param name="FilterFile">Đường dẫn file lọc đã ghi, <c>null</c> nếu không có.</param>
public sealed record SaveJobResultDto(string Name, string? FilterFile);

public sealed record FilterContentDto(string Source, string? FilterFile, string Content);

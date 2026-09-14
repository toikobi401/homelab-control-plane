using Hub.Api.Security;
using Hub.Core.Backup;
using Hub.Core.Results;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

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

        group.MapPost("/upload", UploadAsync)
            .RequireAntiforgery()
            .WithName("UploadBackupFolder")
            .WithSummary("Nhận một lô tệp người dùng tải lên từ trình duyệt");

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
    /// Nhận một lô tệp do trình duyệt tải lên (§5d).
    ///
    /// **Vì sao có endpoint này.** <c>/{jobName}/run</c> chạy một job đã cấu hình
    /// với <c>Source</c> cố định trên máy chủ. Nhưng máy chủ không đọc được đĩa
    /// của máy khác, kể cả trong tailnet — nên muốn sao lưu thư mục của một máy
    /// khác thì mở web UI tại chính máy đó và đẩy nội dung lên đây. Sau đó thư
    /// mục này thành <c>Source</c> của một job thường: một đường lên cloud duy
    /// nhất, không viết đường thứ hai.
    ///
    /// **Đọc bằng <see cref="MultipartReader"/> chứ không dùng
    /// <c>IFormFileCollection</c>/<c>Request.Form</c>**: cả hai đệm toàn bộ body
    /// vào RAM hoặc file tạm trước khi handler chạy. Ở đây byte đi thẳng từ
    /// socket ra đĩa.
    ///
    /// Trả về con số, không trả tên tệp (§6.5 mục 4) — response có thể vào log
    /// truy cập chung.
    /// </summary>
    private static async Task<Results<Ok<UploadResultDto>, ProblemHttpResult>> UploadAsync(
        HttpRequest request,
        UploadReceiver receiver,
        IOptions<BackupOptions> backupOptions,
        CancellationToken cancellationToken)
    {
        if (!MultipartRequestHelper.IsMultipart(request.ContentType))
        {
            return ToProblem(ResultError.Validation("Yêu cầu phải là multipart/form-data."));
        }

        var boundary = MultipartRequestHelper.GetBoundary(request.ContentType);
        if (boundary is null)
        {
            return ToProblem(ResultError.Validation("Thiếu boundary trong multipart/form-data."));
        }

        var limits = backupOptions.Value.Upload;
        var reader = new MultipartReader(boundary, request.Body);

        string? folder = null;
        var paths = new Queue<string>();
        int received = 0, duplicates = 0, rejected = 0, seen = 0;

        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition, out var disposition))
            {
                continue;
            }

            var name = disposition.Name.Value?.Trim('"');

            // Phần text: tên thư mục và danh sách đường dẫn tương đối.
            //
            // Đường dẫn phải gửi riêng vì Content-Disposition chỉ mang tên trần
            // ("img.jpg"), mất hẳn cấu trúc thư mục con — chỉ webkitRelativePath
            // bên JS mới có "Anh/2026/img.jpg".
            if (!disposition.IsFileDisposition())
            {
                var value = await section.ReadAsStringAsync(cancellationToken);

                if (name == "folder") { folder = value; }
                else if (name == "paths") { paths.Enqueue(value); }

                continue;
            }

            if (folder is null)
            {
                return ToProblem(ResultError.Validation("Thiếu tên thư mục đích."));
            }

            if (++seen > limits.MaxFilesPerRequest)
            {
                return ToProblem(ResultError.Validation("Lô tải lên có quá nhiều tệp."));
            }

            var prepared = receiver.PrepareFolder(folder);
            if (prepared.IsFailure)
            {
                return ToProblem(prepared.Error!.Value);
            }

            // Hết đường dẫn tương đối thì rơi về tên tệp trần — client gửi thiếu
            // vẫn nhận được, chỉ mất cấu trúc thư mục con.
            var relativePath = paths.Count > 0
                ? paths.Dequeue()
                : disposition.FileName.Value?.Trim('"') ?? string.Empty;

            var outcome = await receiver.ReceiveFileAsync(
                prepared.Value, relativePath, section.Body, cancellationToken);

            switch (outcome)
            {
                case UploadFileOutcome.Received: received++; break;
                case UploadFileOutcome.Duplicate: duplicates++; break;
                default: rejected++; break;
            }
        }

        if (folder is null)
        {
            return ToProblem(ResultError.Validation("Thiếu tên thư mục đích."));
        }

        return TypedResults.Ok(new UploadResultDto(received, duplicates, rejected, folder));
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

/// <summary>
/// Đọc phần đầu của một request multipart.
///
/// Tách riêng vì <c>Request.Form</c> và <c>IFormFile</c> đều đệm toàn bộ body
/// trước khi handler chạy — thứ ta cố tình tránh ở endpoint tải lên.
/// </summary>
public static class MultipartRequestHelper
{
    public static bool IsMultipart(string? contentType) =>
        !string.IsNullOrEmpty(contentType)
        && contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lấy boundary từ Content-Type. Trả <c>null</c> nếu thiếu hoặc quá dài.
    ///
    /// Giới hạn độ dài theo đúng khuyến nghị của ASP.NET Core: boundary do client
    /// gửi nên không được để nó dài tuỳ ý.
    /// </summary>
    public static string? GetBoundary(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) { return null; }

        var boundary = HeaderUtilities.RemoveQuotes(
            MediaTypeHeaderValue.Parse(contentType).Boundary).Value;

        return string.IsNullOrWhiteSpace(boundary) || boundary.Length > 70 ? null : boundary;
    }
}

/// <param name="Received">Số tệp đã nhận và ghi mới.</param>
/// <param name="Duplicates">Số tệp bỏ qua vì đã có nội dung y hệt (so hash).</param>
/// <param name="Rejected">Số tệp bị từ chối: tên không hợp lệ, quá lớn, hoặc không ghi được.</param>
/// <param name="Folder">
/// Tên thư mục đã nhận, KHÔNG phải đường dẫn tuyệt đối — giao diện gửi lại tên
/// này khi tạo job, và backend tự dựng <c>Source</c> từ <c>UploadRoot</c>.
/// </param>
public sealed record UploadResultDto(int Received, int Duplicates, int Rejected, string Folder);

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

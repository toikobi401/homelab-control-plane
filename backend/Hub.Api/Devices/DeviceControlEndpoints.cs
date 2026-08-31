// ĐÃ THAY THẾ — 2026-08-31.
//
// Năng lực 6 chuyển sang dùng MeshCentral (§2.3: tái sử dụng, đừng phát minh
// lại). Mã dưới đây là bản tự viết trước đó: nó CHẠY ĐƯỢC và có test, nhưng
// không còn là đường chính.
//
// Vì sao chuyển: MeshCentral cho sẵn Wake-on-LAN (phần khó nhất, chưa làm
// được), agent đóng gói cho mọi hệ điều hành, remote desktop, và giao diện
// mobile — đều là thứ tự làm sẽ tốn nhiều công mà không tốt hơn.
//
// Giữ lại tạm thời để còn quay về được nếu MeshCentral không hợp. Đừng xây
// thêm trên mã này; xem docs/meshcentral-setup.md.

using Hub.Api.Authentication;
using Hub.Api.Security;
using Hub.Core.Devices;
using Hub.Core.Results;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Hub.Api.Devices;

/// <summary>
/// Endpoint điều khiển nguồn (§5a — năng lực 6).
///
/// §5a điều 1: **mỗi hành động là một endpoint riêng.** Không có endpoint nhận
/// tham số `action` rồi rẽ nhánh, và tuyệt đối không nhận chuỗi lệnh. Bốn hàm
/// dưới đây trông lặp, và sự lặp đó là có chủ đích — nó khiến việc thêm một
/// hành động mới phải là một quyết định tường minh, không phải một chuỗi mới
/// lọt qua tham số.
///
/// §5a điều 2: chỉ POST, body JSON. Không nhận lệnh qua query string.
/// </summary>
public static class DeviceControlEndpoints
{
    public static IEndpointRouteBuilder MapDeviceControlEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/devices")
            .WithTags("DeviceControl")
            .RequireAuthorization();

        // §6.5 mục 5: endpoint đổi trạng thái phải có antiforgery token, và năng
        // lực 6 là nơi nói rõ "đặc biệt cần". Gắn ở cấp nhóm để endpoint thêm
        // sau này không bị quên.
        var commands = builder.MapGroup("/api/devices")
            .WithTags("DeviceControl")
            .RequireAuthorization()
            .RequireAntiforgery();

        commands.MapPost("/{deviceId:guid}/shutdown", ShutdownAsync)
            .WithName("ShutdownDevice")
            .WithSummary("Tắt máy");

        commands.MapPost("/{deviceId:guid}/restart", RestartAsync)
            .WithName("RestartDevice")
            .WithSummary("Khởi động lại máy");

        commands.MapPost("/{deviceId:guid}/sleep", SleepAsync)
            .WithName("SleepDevice")
            .WithSummary("Cho máy ngủ");

        commands.MapPost("/{deviceId:guid}/lock", LockAsync)
            .WithName("LockDevice")
            .WithSummary("Khoá màn hình");

        group.MapGet("/commands", GetRecentCommandsAsync)
            .WithName("GetDeviceCommands")
            .WithSummary("Nhật ký kiểm toán lệnh điều khiển");

        return builder;
    }

    private static Task<Results<NoContent, ProblemHttpResult>> ShutdownAsync(
        Guid deviceId,
        HttpContext httpContext,
        DeviceControlService service,
        CancellationToken cancellationToken)
        => ExecuteAsync(deviceId, PowerAction.Shutdown, httpContext, service, cancellationToken);

    private static Task<Results<NoContent, ProblemHttpResult>> RestartAsync(
        Guid deviceId,
        HttpContext httpContext,
        DeviceControlService service,
        CancellationToken cancellationToken)
        => ExecuteAsync(deviceId, PowerAction.Restart, httpContext, service, cancellationToken);

    private static Task<Results<NoContent, ProblemHttpResult>> SleepAsync(
        Guid deviceId,
        HttpContext httpContext,
        DeviceControlService service,
        CancellationToken cancellationToken)
        => ExecuteAsync(deviceId, PowerAction.Sleep, httpContext, service, cancellationToken);

    private static Task<Results<NoContent, ProblemHttpResult>> LockAsync(
        Guid deviceId,
        HttpContext httpContext,
        DeviceControlService service,
        CancellationToken cancellationToken)
        => ExecuteAsync(deviceId, PowerAction.Lock, httpContext, service, cancellationToken);

    private static async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(
        Guid deviceId,
        PowerAction action,
        HttpContext httpContext,
        DeviceControlService service,
        CancellationToken cancellationToken)
    {
        // Phiên nào bấm nút — vào nhật ký kiểm toán (§5a điều 7).
        var sessionId = httpContext.GetSessionId();

        var result = await service.ExecuteAsync(deviceId, action, sessionId, cancellationToken);

        return result.IsSuccess
            ? TypedResults.NoContent()
            : ToProblem(result.Error!.Value);
    }

    private static async Task<Ok<List<CommandAuditDto>>> GetRecentCommandsAsync(
        IDeviceStore store,
        CancellationToken cancellationToken)
    {
        var commands = await store.GetRecentCommandsAsync(100, cancellationToken);

        return TypedResults.Ok(commands.Select(audit => new CommandAuditDto(
            audit.Id,
            audit.RequestedAt,
            audit.DeviceHostname,
            audit.Action.ToString(),
            audit.Succeeded,
            audit.FailureReason)).ToList());
    }

    private static ProblemHttpResult ToProblem(ResultError error)
    {
        var statusCode = error.Code switch
        {
            // Chưa duyệt / không tìm thấy thiết bị.
            "validation" => StatusCodes.Status400BadRequest,

            // Chặn bởi quy tắc §5a điều 5 (không tắt máy chạy backend).
            "conflict" => StatusCodes.Status409Conflict,

            "agent_not_configured" => StatusCodes.Status503ServiceUnavailable,

            // Máy đích không phản hồi — lỗi ở đầu kia, không phải request sai.
            "agent_timeout" => StatusCodes.Status504GatewayTimeout,
            "agent_unreachable" => StatusCodes.Status502BadGateway,
            "agent_rejected" => StatusCodes.Status502BadGateway,

            _ => StatusCodes.Status500InternalServerError
        };

        return TypedResults.Problem(title: error.Message, statusCode: statusCode);
    }
}

public sealed record CommandAuditDto(
    int Id,
    DateTimeOffset RequestedAt,
    string DeviceHostname,
    string Action,
    bool Succeeded,
    string? FailureReason);

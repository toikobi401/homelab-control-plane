using Hub.Core.Devices;
using Hub.Core.Results;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Hub.Api.Devices;

/// <summary>
/// Endpoint năng lực 1 — sổ thiết bị và hiện diện, đọc từ Tailscale.
///
/// Mọi endpoint đều yêu cầu đăng nhập. §6.4: tailnet là lớp phòng thủ thứ nhất,
/// không phải duy nhất — một điện thoại mất mà đang mở phiên là đã vượt cả hai
/// lớp, nên không có ngoại lệ kiểu "gọi từ 100.x thì bỏ qua xác thực".
/// </summary>
public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/devices")
            .WithTags("Devices")
            .RequireAuthorization();

        group.MapGet("/", GetDevicesAsync)
            .WithName("GetDevices")
            .WithSummary("Danh sách thiết bị trong tailnet kèm trạng thái hiện diện");

        group.MapGet("/{deviceId}", GetDeviceAsync)
            .WithName("GetDevice")
            .WithSummary("Chi tiết một thiết bị");

        return builder;
    }

    // Kiểu trả về khai tường minh để OpenAPI suy được schema, và frontend sinh
    // kiểu TypeScript từ đó (§3).
    private static async Task<Results<Ok<DeviceListDto>, ProblemHttpResult>> GetDevicesAsync(
        ITailnetClient tailnetClient,
        CancellationToken cancellationToken)
    {
        var result = await tailnetClient.GetDevicesAsync(cancellationToken);

        if (result.IsFailure)
        {
            return ToProblem(result.Error!.Value);
        }

        var devices = result.Value.Select(ToDto).ToList();
        return TypedResults.Ok(new DeviceListDto(devices, devices.Count(device => device.IsOnline)));
    }

    private static async Task<Results<Ok<DeviceDto>, NotFound, ProblemHttpResult>> GetDeviceAsync(
        string deviceId,
        ITailnetClient tailnetClient,
        CancellationToken cancellationToken)
    {
        var result = await tailnetClient.GetDevicesAsync(cancellationToken);

        if (result.IsFailure)
        {
            return ToProblem(result.Error!.Value);
        }

        var device = result.Value.FirstOrDefault(candidate => candidate.Id == deviceId);

        return device is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToDto(device));
    }

    private static DeviceDto ToDto(TailnetDevice device) => new(
        device.Id,
        device.Hostname,
        device.Name,
        device.OperatingSystem,
        device.TailnetAddress,
        device.LastSeen,
        device.IsOnline,
        device.Authorized,
        device.IsExternal,
        device.ClientVersion,
        device.UpdateAvailable);

    /// <summary>
    /// §6.5 mục 7: không lộ chi tiết lỗi. Thông báo ở đây do chính hệ thống
    /// soạn (không phải chuyển tiếp lỗi của Tailscale), nên an toàn để hiện.
    /// </summary>
    private static ProblemHttpResult ToProblem(ResultError error)
    {
        var statusCode = error.Code switch
        {
            // Chưa cấu hình là lỗi vận hành, không phải lỗi người dùng —
            // 503 để frontend phân biệt được với "hỏng tạm thời".
            "tailscale_not_configured" => StatusCodes.Status503ServiceUnavailable,
            "tailscale_auth_failed" => StatusCodes.Status502BadGateway,
            "tailscale_unavailable" => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        };

        return TypedResults.Problem(title: error.Message, statusCode: statusCode);
    }
}

public sealed record DeviceListDto(List<DeviceDto> Devices, int OnlineCount);

public sealed record DeviceDto(
    string Id,
    string Hostname,
    string Name,
    string OperatingSystem,
    string? TailnetAddress,
    DateTimeOffset? LastSeen,
    bool IsOnline,
    bool Authorized,
    bool IsExternal,
    string? ClientVersion,
    bool UpdateAvailable);

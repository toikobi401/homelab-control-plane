using Hub.Api.Security;
using Hub.Core.Devices;
using Hub.Core.Results;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hub.Api.Devices;

/// <summary>Sổ đăng ký thiết bị (§5a): agent tự đăng ký, người dùng duyệt.</summary>
public static class DeviceRegistryEndpoints
{
    public static IEndpointRouteBuilder MapDeviceRegistryEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/devices").WithTags("DeviceRegistry");

        // Agent đăng ký. KHÔNG dùng cookie đăng nhập — agent là dịch vụ chạy
        // nền, không có phiên người dùng. Xác thực bằng khoá chung thay thế.
        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .WithName("RegisterDevice")
            .WithSummary("Agent tự đăng ký (xác thực bằng khoá chung)");

        group.MapGet("/registered", GetRegisteredAsync)
            .RequireAuthorization()
            .WithName("GetRegisteredDevices")
            .WithSummary("Danh sách thiết bị đã đăng ký kèm trạng thái duyệt");

        group.MapPost("/{deviceId:guid}/approve", ApproveAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .WithName("ApproveDevice")
            .WithSummary("Duyệt thiết bị để nó nhận được lệnh");

        group.MapPost("/{deviceId:guid}/revoke-approval", RevokeApprovalAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .WithName("RevokeDeviceApproval")
            .WithSummary("Thu hồi duyệt");

        group.MapDelete("/{deviceId:guid}", DeleteAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .WithName("DeleteDevice")
            .WithSummary("Gỡ thiết bị khỏi sổ đăng ký");

        return builder;
    }

    private static async Task<Results<Ok<RegisteredDeviceDto>, ProblemHttpResult>> RegisterAsync(
        [FromBody] RegisterDeviceRequest request,
        HttpContext httpContext,
        DeviceRegistryService registry,
        IOptions<AgentOptions> agentOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DeviceRegistration");
        var expected = agentOptions.Value.SharedSecret;

        // §6.4: tailnet không phải lớp phòng thủ duy nhất. Không có khoá chung
        // thì bất kỳ thiết bị nào trong tailnet cũng tự đăng ký được — và tuy
        // vẫn phải chờ duyệt, đó vẫn là rác đổ vào sổ đăng ký.
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError("Chưa cấu hình Agent:SharedSecret — từ chối đăng ký.");
            return TypedResults.Problem(
                title: "Chưa cấu hình khoá chung với agent.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!IsSecretValid(httpContext, expected))
        {
            logger.LogWarning(
                "Đăng ký thiết bị bị từ chối: khoá chung sai (từ {Address}).",
                httpContext.Connection.RemoteIpAddress);

            return TypedResults.Problem(
                title: "Khoá chung không hợp lệ.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await registry.RegisterAsync(
            new DeviceRegistration
            {
                Hostname = request.Hostname,
                OperatingSystem = request.OperatingSystem,

                // Lấy địa chỉ từ chính kết nối, không tin giá trị agent tự khai —
                // đây là thứ backend gửi lệnh tới, nên phải là địa chỉ thật.
                TailnetAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                MacAddress = request.MacAddress,
                LanLabel = request.LanLabel,
                IsBackendHost = request.IsBackendHost,
                FromAgent = request.FromAgent
            },
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ToDto(result.Value))
            : TypedResults.Problem(
                title: result.Error!.Value.Message,
                statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// So sánh khoá theo thời gian cố định. So sánh chuỗi thường thoát sớm ở
    /// byte đầu khác nhau, để lộ thông tin qua thời gian phản hồi.
    /// </summary>
    private static bool IsSecretValid(HttpContext httpContext, string expected)
    {
        var header = httpContext.Request.Headers.Authorization.ToString();

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var provided = header[prefix.Length..];

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }

    private static async Task<Ok<List<RegisteredDeviceDto>>> GetRegisteredAsync(
        DeviceRegistryService registry,
        CancellationToken cancellationToken)
    {
        var devices = await registry.GetAllAsync(cancellationToken);
        return TypedResults.Ok(devices.Select(ToDto).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ApproveAsync(
        Guid deviceId,
        DeviceRegistryService registry,
        CancellationToken cancellationToken)
    {
        var result = await registry.ApproveAsync(deviceId, cancellationToken);
        return ToResponse(result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeApprovalAsync(
        Guid deviceId,
        DeviceRegistryService registry,
        CancellationToken cancellationToken)
    {
        var result = await registry.RevokeApprovalAsync(deviceId, cancellationToken);
        return ToResponse(result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid deviceId,
        DeviceRegistryService registry,
        CancellationToken cancellationToken)
    {
        var result = await registry.DeleteAsync(deviceId, cancellationToken);
        return ToResponse(result);
    }

    private static Results<NoContent, ProblemHttpResult> ToResponse(Result result)
        => result.IsSuccess
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                title: result.Error!.Value.Message,
                statusCode: StatusCodes.Status400BadRequest);

    private static RegisteredDeviceDto ToDto(RegisteredDevice device) => new(
        device.Id,
        device.Hostname,
        device.OperatingSystem,
        device.TailnetAddress,
        device.MacAddress,
        device.LanLabel,
        device.IsApproved,
        device.IsBackendHost,
        device.RegisteredAt,
        device.LastSeenAt,
        device.AgentLastSeenAt);
}

public sealed record RegisterDeviceRequest(
    string Hostname,
    string OperatingSystem,
    string? MacAddress,
    string? LanLabel,
    bool IsBackendHost,
    bool FromAgent = false);

public sealed record RegisteredDeviceDto(
    Guid Id,
    string Hostname,
    string OperatingSystem,
    string? TailnetAddress,
    string? MacAddress,
    string? LanLabel,
    bool IsApproved,
    bool IsBackendHost,
    DateTimeOffset RegisteredAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? AgentLastSeenAt);

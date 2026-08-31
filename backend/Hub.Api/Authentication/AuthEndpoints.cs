using System.Security.Claims;
using Hub.Api.Security;
using Microsoft.AspNetCore.RateLimiting;
using Hub.Core.Authentication;
using Hub.Core.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Api.Authentication;

/// <summary>Endpoint xác thực (§6.3). Mỗi endpoint làm đúng một việc (§7).</summary>
public static class AuthEndpoints
{
    /// <summary>Tên claim giữ id phiên trong cookie.</summary>
    public const string SessionIdClaim = "hub:sid";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/status", GetStatusAsync)
            .AllowAnonymous()
            .WithSummary("Hệ thống đã đặt mật khẩu chưa");

        group.MapPost("/setup", SetupAsync)
            .AllowAnonymous()
            .RequireAntiforgery()
            .RequireRateLimiting(RateLimiting.AuthPolicy)
            .WithSummary("Đặt mật khẩu lần đầu (chỉ từ localhost)");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireAntiforgery()
            .RequireRateLimiting(RateLimiting.AuthPolicy)
            .WithSummary("Đăng nhập");

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .WithSummary("Đăng xuất phiên hiện tại");

        group.MapGet("/sessions", GetSessionsAsync)
            .RequireAuthorization()
            .WithSummary("Liệt kê phiên đang mở");

        group.MapDelete("/sessions/{sessionId:guid}", RevokeSessionAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .WithSummary("Thu hồi một phiên");

        group.MapPost("/sessions/revoke-all", RevokeAllSessionsAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .WithSummary("Đăng xuất tất cả thiết bị");

        group.MapPost("/password", ChangePasswordAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .WithSummary("Đổi mật khẩu");

        return builder;
    }

    // Kiểu trả về khai tường minh (Ok<T>), không phải IResult: OpenAPI suy schema
    // từ chữ ký, và frontend sinh kiểu TypeScript từ schema đó (§3). Khai IResult
    // thì spec ra response rỗng và kiểu sinh ra không có dữ liệu trả về.
    private static async Task<Ok<AuthStatus>> GetStatusAsync(
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var status = await authService.GetStatusAsync(cancellationToken);
        return TypedResults.Ok(status);
    }

    private static async Task<IResult> SetupAsync(
        [FromBody] SetPasswordRequest request,
        HttpContext httpContext,
        AuthService authService,
        LocalSetupPolicy setupPolicy,
        CancellationToken cancellationToken)
    {
        // §6.3: không cho đặt mật khẩu lần đầu từ xa.
        if (!setupPolicy.IsAllowed(httpContext))
        {
            return Results.Problem(
                title: "Chỉ đặt được mật khẩu lần đầu từ máy chạy hệ thống.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await authService.SetInitialPasswordAsync(request.Password, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToProblem(result.Error!.Value);
    }

    private static async Task<Results<Ok<SessionDto>, ProblemHttpResult>> LoginAsync(
        [FromBody] LoginRequest request,
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(
            request.Password,
            httpContext.GetDeviceDescription(),
            httpContext.GetCallerAddress(),
            cancellationToken);

        if (result.IsFailure)
        {
            return ToProblem(result.Error!.Value);
        }

        var session = result.Value.Session;

        // Cookie mang id phiên; phiên thật nằm trong DB nên thu hồi được (§6.3).
        var identity = new ClaimsIdentity(
            [new Claim(SessionIdClaim, session.Id.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = session.ExpiresAt
            });

        return TypedResults.Ok(ToDto(session, currentSessionId: session.Id));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var sessionId = httpContext.GetSessionId();
        if (sessionId is not null)
        {
            await authService.RevokeSessionAsync(sessionId.Value, cancellationToken);
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<Ok<List<SessionDto>>> GetSessionsAsync(
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var current = httpContext.GetSessionId();
        var sessions = await authService.GetActiveSessionsAsync(cancellationToken);

        return TypedResults.Ok(sessions.Select(session => ToDto(session, current)).ToList());
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid sessionId,
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.RevokeSessionAsync(sessionId, cancellationToken);

        if (result.IsFailure)
        {
            return ToProblem(result.Error!.Value);
        }

        // Tự thu hồi phiên của chính mình thì phải xoá cookie luôn, nếu không
        // trình duyệt còn giữ cookie trỏ tới phiên đã chết.
        if (httpContext.GetSessionId() == sessionId)
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return Results.NoContent();
    }

    private static async Task<Ok<RevokeAllResponse>> RevokeAllSessionsAsync(
        [FromQuery] bool keepCurrent,
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var current = httpContext.GetSessionId();
        var except = keepCurrent ? current : null;

        var count = await authService.RevokeAllSessionsAsync(except, cancellationToken);

        if (!keepCurrent)
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return TypedResults.Ok(new RevokeAllResponse(count));
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        // Giữ lại phiên hiện tại; §6.3 yêu cầu huỷ mọi phiên KHÁC.
        var current = httpContext.GetSessionId();

        var result = await authService.ChangePasswordAsync(
            request.CurrentPassword,
            request.NewPassword,
            current,
            cancellationToken);

        return result.IsSuccess ? Results.NoContent() : ToProblem(result.Error!.Value);
    }

    private static SessionDto ToDto(Session session, Guid? currentSessionId) => new(
        session.Id,
        session.Device,
        session.TailnetAddress,
        session.CreatedAt,
        session.LastSeenAt,
        session.ExpiresAt,
        session.Id == currentSessionId);

    /// <summary>
    /// Đổi lỗi nghiệp vụ thành mã HTTP. §6.5 mục 7: chỉ trả thông báo chung,
    /// chi tiết nằm ở log.
    /// </summary>
    private static ProblemHttpResult ToProblem(ResultError error)
    {
        var statusCode = error.Code switch
        {
            "unauthorized" => StatusCodes.Status401Unauthorized,
            "too_many_attempts" => StatusCodes.Status429TooManyRequests,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return TypedResults.Problem(title: error.Message, statusCode: statusCode);
    }
}

public sealed record SetPasswordRequest(string Password);

public sealed record LoginRequest(string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record RevokeAllResponse(int RevokedCount);

public sealed record SessionDto(
    Guid Id,
    string Device,
    string? TailnetAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);


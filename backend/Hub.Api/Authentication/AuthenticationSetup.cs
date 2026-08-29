using System.Security.Claims;
using Hub.Core.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Hub.Api.Authentication;

public static class AuthenticationSetup
{
    /// <summary>Id phiên lấy từ cookie đã xác thực.</summary>
    public static Guid? GetSessionId(this HttpContext context)
    {
        var raw = context.User.FindFirstValue(AuthEndpoints.SessionIdClaim);
        return Guid.TryParse(raw, out var sessionId) ? sessionId : null;
    }

    public static IServiceCollection AddHubAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                // §6.3: cookie bắt buộc HttpOnly, Secure, SameSite=Strict.
                options.Cookie.Name = "hub_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;

                // Always chứ không phải SameAsRequest: §4 quy định HTTPS bắt
                // buộc, nên cookie không bao giờ được đi qua HTTP trần.
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;

                // Đây là API, không phải app có trang đăng nhập server-side.
                // Mặc định ASP.NET trả 302 về /Account/Login — với API thì phải
                // là 401/403 để frontend xử lý được.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };

                // ĐIỂM MẤU CHỐT của §6.3: cookie đã ký thôi thì CHƯA đủ. Mỗi
                // request phải đối chiếu phiên trong DB, nếu không "đăng xuất
                // tất cả thiết bị" chỉ là hình thức — cookie cũ vẫn dùng được
                // cho tới khi hết hạn.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var sessionId = context.Principal?.FindFirstValue(AuthEndpoints.SessionIdClaim);

                    if (!Guid.TryParse(sessionId, out var parsed))
                    {
                        context.RejectPrincipal();
                        return;
                    }

                    var authService = context.HttpContext.RequestServices
                        .GetRequiredService<AuthService>();

                    var result = await authService.ValidateSessionAsync(
                        parsed, context.HttpContext.RequestAborted);

                    if (result.IsFailure)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(
                            CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }
}

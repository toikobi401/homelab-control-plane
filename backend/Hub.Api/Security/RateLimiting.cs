using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Api.Security;

/// <summary>
/// Giới hạn tần suất request.
///
/// Trước đây tailnet là lớp chặn: chỉ thiết bị đã cài Tailscale mới gọi tới
/// được, nên không ai dội request vào hệ thống. Khi mở ra Internet, lớp đó biến
/// mất — mọi bot quét cổng đều chạm tới được màn hình đăng nhập.
///
/// §6.3 đã có khoá tăng dần cho việc đoán mật khẩu, nhưng nó chỉ áp cho
/// <c>/api/auth/login</c> và chỉ đếm số lần SAI. Rate limit ở đây chặn thứ khác:
/// dội request vào bất kỳ endpoint nào, kể cả request hợp lệ.
/// </summary>
public static class RateLimiting
{
    /// <summary>Chính sách cho endpoint xác thực — chặt hơn hẳn.</summary>
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddHubRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // 429 kèm Retry-After để client biết chờ bao lâu, thay vì thử lại ngay.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();
                }

                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting")
                    .LogWarning(
                        "Chặn vì quá tần suất: {Path} từ {Address}",
                        context.HttpContext.Request.Path,
                        context.HttpContext.Connection.RemoteIpAddress);

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { title = "Quá nhiều yêu cầu. Thử lại sau ít phút." },
                    cancellationToken);
            };

            // Giới hạn chung cho mọi request, theo địa chỉ gọi tới.
            //
            // 300/phút nghe nhiều, nhưng một lần mở giao diện đã tải hàng chục
            // file tĩnh — đặt thấp quá thì chính người dùng bị chặn.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Endpoint xác thực: chặt hơn nhiều. Đây là chỗ bot nhắm tới.
            //
            // 10 lần/phút đủ cho người thật gõ nhầm vài lần, nhưng cắt đứt việc
            // thử hàng nghìn mật khẩu. Kết hợp với khoá tăng dần của §6.3.
            options.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    /// <summary>
    /// Khoá phân vùng: địa chỉ IP của người gọi.
    ///
    /// Sau Cloudflare Tunnel, RemoteIpAddress là địa chỉ của tunnel chứ không
    /// phải khách thật — mọi người sẽ chung một phân vùng. Vì vậy ưu tiên
    /// <c>CF-Connecting-IP</c>, header do chính Cloudflare đặt và ghi đè giá trị
    /// client gửi lên.
    ///
    /// KHÔNG dùng X-Forwarded-For: client tự đặt được, nên kẻ tấn công chỉ cần
    /// đổi header mỗi request là thoát rate limit hoàn toàn.
    /// </summary>
    private static string GetClientKey(HttpContext context)
    {
        var cloudflareIp = context.Request.Headers["CF-Connecting-IP"].ToString();

        if (!string.IsNullOrWhiteSpace(cloudflareIp))
        {
            return cloudflareIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

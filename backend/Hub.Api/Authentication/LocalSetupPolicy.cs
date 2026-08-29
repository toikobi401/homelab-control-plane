using Microsoft.Extensions.Options;

namespace Hub.Api.Authentication;

/// <summary>
/// Quyết định request có được phép **đặt mật khẩu lần đầu** không (§6.3: chỉ
/// chấp nhận từ localhost, không cho đặt từ xa).
///
/// Vì sao cần cả một lớp cho một câu if: khi backend chạy trong container,
/// Docker NAT khiến mọi request đến từ gateway của mạng bridge (172.17.0.1),
/// nên kiểm tra loopback luôn sai và **không ai đặt được mật khẩu lần đầu**.
///
/// Cách giải SAI mà ta cố tình không dùng:
/// - Tin <c>X-Forwarded-For</c>: client tự đặt được header này, ai cũng giả
///   thành 127.0.0.1 → thủng hoàn toàn.
/// - Bỏ luôn kiểm tra khi ở trong container: mở cửa cho mọi thiết bị trong
///   tailnet đặt mật khẩu đầu tiên, tức là chiếm hệ thống.
///
/// Cách giải dùng ở đây: trong container, người vận hành phải khai báo tường
/// minh dải mạng được phép qua <c>HUB_SETUP_ALLOWED_NETWORK</c> (thường là
/// gateway của bridge). Không khai thì không ai đặt được — thất bại đóng
/// (fail closed), đúng tinh thần §4.
/// </summary>
public sealed class LocalSetupPolicy(
    IOptions<SetupOptions> options,
    ILogger<LocalSetupPolicy> logger)
{
    private readonly SetupOptions _options = options.Value;

    public bool IsAllowed(HttpContext context)
    {
        if (context.IsLoopback())
        {
            return true;
        }

        var caller = context.Connection.RemoteIpAddress;
        var allowed = _options.AllowedNetwork;

        if (string.IsNullOrWhiteSpace(allowed))
        {
            logger.LogWarning(
                "Từ chối đặt mật khẩu lần đầu từ {Caller}: không phải localhost và chưa khai " +
                "HUB_SETUP_ALLOWED_NETWORK.", caller);
            return false;
        }

        if (caller is not null && string.Equals(
                caller.ToString(), allowed, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Cho phép đặt mật khẩu lần đầu từ {Caller} theo HUB_SETUP_ALLOWED_NETWORK.",
                caller);
            return true;
        }

        logger.LogWarning("Từ chối đặt mật khẩu lần đầu từ {Caller}.", caller);
        return false;
    }
}

public sealed class SetupOptions
{
    public const string SectionName = "Setup";

    /// <summary>
    /// Địa chỉ duy nhất (ngoài loopback) được phép đặt mật khẩu lần đầu.
    /// Chỉ dùng khi chạy trong container; để trống khi chạy thẳng trên máy.
    /// </summary>
    public string? AllowedNetwork { get; set; }
}

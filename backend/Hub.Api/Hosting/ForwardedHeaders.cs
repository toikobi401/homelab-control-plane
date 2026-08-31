using Microsoft.AspNetCore.HttpOverrides;

namespace Hub.Api.Hosting;

/// <summary>
/// Nhận diện HTTPS khi hub chạy sau Cloudflare Tunnel.
///
/// Cloudflare kết thúc TLS ở biên rồi gọi hub bằng **HTTP** trên loopback. Hub
/// thấy <c>Request.IsHttps == false</c> và hai thứ hỏng theo:
///
/// - Cookie antiforgery đặt <c>SecurePolicy = Always</c> nên ASP.NET từ chối
///   ghi nó, ném <c>InvalidOperationException</c> → mọi POST trả 500.
/// - Header HSTS không được đặt vì ta chỉ đặt nó trên kết nối HTTPS.
///
/// Đã gặp thật: đăng nhập qua tunnel trả 500 và thiếu HSTS.
///
/// <c>X-Forwarded-Proto</c> giải quyết được, nhưng nó là header **client tự đặt
/// được**. Tin nó bừa nghĩa là ai cũng giả được "tôi đang dùng HTTPS" — vô hiệu
/// hoá luôn ý nghĩa của cờ Secure trên cookie.
///
/// Vì vậy chỉ bật ở chế độ <see cref="BindMode.Tunnel"/>, nơi hub **chỉ nghe
/// loopback**: request duy nhất tới được là từ cloudflared trên chính máy này.
/// Không ai ngoài máy gửi header giả vào được.
/// </summary>
public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddTunnelForwardedHeaders(
        this IServiceCollection services,
        BindMode bindMode)
    {
        if (bindMode is not BindMode.Tunnel)
        {
            // Các chế độ khác nghe trên địa chỉ mạng thật — tin header chuyển
            // tiếp ở đó là mở đường cho giả mạo.
            return services;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;

            // Mặc định ASP.NET chỉ tin proxy trong danh sách known. Ở đây proxy
            // là cloudflared trên loopback, và ta đã biết chắc điều đó vì chế độ
            // Tunnel không nghe địa chỉ nào khác — nên xoá danh sách để nó
            // không loại bỏ header.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }
}

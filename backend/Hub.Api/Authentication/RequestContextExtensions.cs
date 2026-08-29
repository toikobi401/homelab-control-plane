using System.Net;

namespace Hub.Api.Authentication;

public static class RequestContextExtensions
{
    /// <summary>
    /// Địa chỉ IP của người gọi, dạng chuỗi để ghi vào phiên (§6.3).
    /// </summary>
    public static string? GetCallerAddress(this HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Mô tả thiết bị lấy từ User-Agent. Chỉ để người dùng nhận ra máy nào trong
    /// danh sách phiên — không dùng cho quyết định bảo mật nào (client sửa được).
    /// </summary>
    public static string GetDeviceDescription(this HttpContext context)
    {
        var userAgent = context.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? "Thiết bị không rõ" : userAgent;
    }

    /// <summary>
    /// Request có đến từ chính máy chạy backend không (§6.3: đặt mật khẩu lần
    /// đầu chỉ chấp nhận từ localhost).
    ///
    /// Lưu ý khi chạy trong container: Docker NAT làm mọi request đến từ
    /// gateway của mạng bridge, nên loopback check sẽ luôn sai. Xem
    /// <see cref="LocalSetupPolicy"/> — không nới lỏng kiểm tra này bằng cách
    /// tin header do client gửi (X-Forwarded-For giả được).
    /// </summary>
    public static bool IsLoopback(this HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return false;
        }

        // Kestrel biểu diễn IPv4 loopback qua IPv6 là ::ffff:127.0.0.1.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address);
    }
}

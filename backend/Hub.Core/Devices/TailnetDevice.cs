namespace Hub.Core.Devices;

/// <summary>
/// Một thiết bị trong tailnet, đã chuẩn hoá từ dữ liệu Tailscale trả về.
///
/// Đây là model của năng lực 1 (hiện diện). Nó KHÔNG phải sổ đăng ký thiết bị
/// đầy đủ của §5a: Tailscale không biết địa chỉ MAC, nhãn LAN, hay khả năng
/// đánh thức — những thứ đó chỉ agent chạy trên máy mới báo được. Khi làm năng
/// lực 6, sổ đăng ký sẽ hợp nhất hai nguồn, không thay thế nguồn này.
/// </summary>
public sealed record TailnetDevice
{
    /// <summary>Định danh ổn định do Tailscale cấp (trường <c>nodeId</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Tên máy hiển thị trong admin console.</summary>
    public required string Hostname { get; init; }

    /// <summary>Tên MagicDNS đầy đủ, ví dụ <c>pc.tailnet-example.ts.net</c>.</summary>
    public required string Name { get; init; }

    public required string OperatingSystem { get; init; }

    /// <summary>Địa chỉ tailnet IPv4 (100.x.y.z). Null nếu thiết bị chỉ có IPv6.</summary>
    public string? TailnetAddress { get; init; }

    /// <summary>Lần cuối thiết bị còn hoạt động trên tailnet.</summary>
    public DateTimeOffset? LastSeen { get; init; }

    /// <summary>
    /// Suy ra từ <see cref="LastSeen"/>, KHÔNG phải Tailscale trả về.
    ///
    /// API danh sách thiết bị không có trường "online" — chỉ có lastSeen. Vì
    /// vậy đây là phỏng đoán theo ngưỡng, và phải được trình bày đúng như thế
    /// ở giao diện: "thấy lần cuối X phút trước", không phải khẳng định chắc
    /// chắn máy đang bật.
    /// </summary>
    public required bool IsOnline { get; init; }

    /// <summary>Đã được duyệt vào tailnet chưa (device authorization của Tailscale).</summary>
    public bool Authorized { get; init; }

    /// <summary>
    /// Thiết bị được chia sẻ vào tailnet từ tài khoản khác, không phải thành
    /// viên. Hệ thống một người dùng nên bình thường luôn là false.
    /// </summary>
    public bool IsExternal { get; init; }

    /// <summary>Phiên bản client Tailscale; rỗng với thiết bị ngoài.</summary>
    public string? ClientVersion { get; init; }

    /// <summary>Có bản cập nhật Tailscale không.</summary>
    public bool UpdateAvailable { get; init; }
}

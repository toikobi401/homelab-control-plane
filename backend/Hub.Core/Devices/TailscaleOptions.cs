namespace Hub.Core.Devices;

/// <summary>
/// Cấu hình truy cập Tailscale API.
///
/// §6.5 mục 1: KHÔNG secret trong source. ClientSecret phải đến từ .NET User
/// Secrets lúc phát triển, hoặc biến môi trường lúc chạy thật.
/// </summary>
public sealed class TailscaleOptions
{
    public const string SectionName = "Tailscale";

    /// <summary>
    /// Tên tailnet. Giá trị "-" nghĩa là "tailnet mặc định của credential đang
    /// dùng" — đúng cho hệ thống một người dùng, khỏi phải khai tên thật.
    /// </summary>
    public string Tailnet { get; set; } = "-";

    /// <summary>OAuth client ID, lấy ở admin console.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret. Không bao giờ commit giá trị này.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Ngưỡng coi là online, tính từ lastSeen. Tailscale không trả trường
    /// "online" trong danh sách thiết bị nên trạng thái phải suy ra.
    ///
    /// 5 phút: client Tailscale gửi tín hiệu đều hơn thế nhiều, nhưng để rộng
    /// tay cho điện thoại đang ngủ và mạng chập chờn.
    /// </summary>
    public TimeSpan OnlineThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Thời gian giữ cache danh sách thiết bị. Tránh gọi Tailscale mỗi lần
    /// người dùng mở trang — API có giới hạn tần suất, và dữ liệu hiện diện
    /// không cần chính xác tới từng giây.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Đã khai đủ thông tin để gọi API chưa.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

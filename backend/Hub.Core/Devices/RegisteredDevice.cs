namespace Hub.Core.Devices;

/// <summary>
/// Thiết bị đã đăng ký với hub qua agent (§5a, sổ đăng ký thiết bị).
///
/// Khác với <see cref="TailnetDevice"/> — cái đó đọc từ Tailscale và biết được
/// hiện diện, nhưng không biết MAC, subnet, hay khả năng đánh thức. Chỉ agent
/// chạy trên chính máy đó mới báo được những thứ này.
///
/// §5a: hai năng lực (1 và 6) dùng chung một sổ, không tạo hai sổ riêng. Ghép
/// lại ở <see cref="DeviceView"/>.
/// </summary>
public sealed class RegisteredDevice
{
    /// <summary>Id ổn định do agent sinh lần đầu và lưu lại phía agent.</summary>
    public required Guid Id { get; set; }

    public required string Hostname { get; set; }

    public required string OperatingSystem { get; set; }

    /// <summary>Địa chỉ tailnet agent tự báo, để đối chiếu với dữ liệu Tailscale.</summary>
    public string? TailnetAddress { get; set; }

    /// <summary>
    /// Địa chỉ MAC, chuẩn hoá dạng AA:BB:CC:DD:EE:FF. Bắt buộc để đánh thức, và
    /// phải ghi lại lúc agent còn online — máy đã tắt thì không hỏi được nữa (§5a).
    /// </summary>
    public string? MacAddress { get; set; }

    /// <summary>
    /// Nhãn LAN suy từ subnet agent báo về. Máy cùng nhãn thì đánh thức được cho
    /// nhau (§5a.1) — magic packet là broadcast tầng 2, không qua router.
    /// </summary>
    public string? LanLabel { get; set; }

    /// <summary>
    /// §5a: thiết bị mới phải được duyệt thủ công một lần trước khi nhận lệnh.
    /// Agent đăng ký xong là ở trạng thái chờ duyệt.
    /// </summary>
    public bool IsApproved { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Máy này có phải máy đang chạy backend không. §5a điều 5: không cho tắt
    /// máy đang phục vụ chính request đó.
    /// </summary>
    public bool IsBackendHost { get; set; }

    /// <summary>
    /// Lần cuối **agent** báo danh — khác <see cref="LastSeenAt"/>, cái đó cũng
    /// được cập nhật khi đăng ký bằng script.
    ///
    /// Dùng để phân biệt "agent chưa bao giờ chạy" với "agent từng chạy rồi im".
    /// Hai tình huống đó cần hai hướng dẫn khác nhau, và đoán sai thì người dùng
    /// đi tìm nhầm chỗ — đã gặp thật: máy đang bật mà báo "có thể đã tắt".
    /// </summary>
    public DateTimeOffset? AgentLastSeenAt { get; set; }
}

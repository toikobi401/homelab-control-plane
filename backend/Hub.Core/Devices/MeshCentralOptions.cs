namespace Hub.Core.Devices;

/// <summary>
/// Cấu hình MeshCentral — công cụ quản lý thiết bị mà hub nhúng vào (§2.3:
/// "tái sử dụng giao thức, đừng phát minh lại").
///
/// Vì sao dùng lại thay vì tự viết: MeshCentral đã có agent đóng gói sẵn cho
/// Windows/Linux/macOS, điều khiển nguồn, **Wake-on-LAN**, remote desktop, và
/// một giao diện mobile riêng. Tự làm những thứ đó là viết lại công cụ đã được
/// kiểm chứng — đúng thứ §2.3 nói sẽ bị từ chối.
///
/// Hub vẫn là **control plane và UI** (§2.3): nó giữ đăng nhập (§6), bố cục, và
/// các năng lực khác; phần thiết bị thì nhúng MeshCentral vào một tab.
/// </summary>
public sealed class MeshCentralOptions
{
    public const string SectionName = "MeshCentral";

    /// <summary>
    /// Địa chỉ MeshCentral trong tailnet, ví dụ
    /// <c>https://hub.tailnet-example.ts.net:4430</c>.
    ///
    /// Phải là địa chỉ mà **trình duyệt** của người dùng gọi tới được, không phải
    /// localhost của máy chạy hub — iframe chạy trên máy người dùng.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Địa chỉ MeshCentral qua Internet công khai, ví dụ
    /// <c>https://mesh.example.com</c>.
    ///
    /// Vì sao cần hai địa chỉ: tên MagicDNS (<see cref="Url"/>) **chỉ phân giải
    /// được từ thiết bị đã cài Tailscale**. Người dùng mở hub qua domain công
    /// khai sẽ nhận "Không thể tìm thấy địa chỉ IP của máy chủ" — đã gặp thật.
    /// Đây cũng thường là địa chỉ duy nhất có chứng chỉ hợp lệ, nên iframe không
    /// bị chặn vì chứng chỉ tự ký.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Chọn địa chỉ hợp với nơi người dùng đang truy cập.
    ///
    /// Quy tắc: vào hub qua tailnet thì dùng địa chỉ tailnet; vào qua đường nào
    /// khác thì dùng địa chỉ công khai. Thiếu cái nào thì rơi về cái còn lại —
    /// một địa chỉ sai vẫn hơn không có gì, vì giao diện còn nút "mở tab mới".
    /// </summary>
    /// <param name="requestIsTailnet">
    /// Request tới hub có đi qua tailnet không (suy từ Host của request).
    /// </param>
    public string? ResolveUrl(bool requestIsTailnet) =>
        requestIsTailnet
            ? Url ?? PublicUrl
            : PublicUrl ?? Url;

    /// <summary>
    /// Đã khai đủ để nhúng chưa. Chưa khai thì giao diện hiện hướng dẫn cài đặt
    /// thay vì một iframe trắng.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) || !string.IsNullOrWhiteSpace(PublicUrl);
}

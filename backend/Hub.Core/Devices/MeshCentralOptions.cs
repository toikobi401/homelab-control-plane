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
    /// Địa chỉ MeshCentral, ví dụ <c>https://hub.tailnet-example.ts.net:4430</c>.
    ///
    /// Phải là địa chỉ mà **trình duyệt** của người dùng gọi tới được, không phải
    /// localhost của máy chạy hub — iframe chạy trên máy người dùng.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Đã khai đủ để nhúng chưa. Chưa khai thì giao diện hiện hướng dẫn cài đặt
    /// thay vì một iframe trắng.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);
}

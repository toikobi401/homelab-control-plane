namespace Hub.Core.Devices;

/// <summary>
/// Tập hành động điều khiển nguồn. **Đóng, không mở rộng** (§5a).
///
/// Là enum chứ không phải chuỗi: agent nhận đúng một trong các giá trị này và
/// ánh xạ sang lệnh Windows đã định sẵn. Không có đường nào để một chuỗi từ
/// người dùng đi tới chỗ thực thi.
///
/// §5a cấm tuyệt đối việc thêm hành động "chạy lệnh tuỳ ý" vào đây. Nếu sau này
/// cần một việc mới, thêm một giá trị chuyên biệt cho đúng việc đó.
/// </summary>
public enum PowerAction
{
    Shutdown,
    Restart,
    Sleep,
    Lock
}

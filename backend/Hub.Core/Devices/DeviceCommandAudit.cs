namespace Hub.Core.Devices;

/// <summary>
/// Nhật ký kiểm toán cho mọi lệnh điều khiển (§5a điều 7).
///
/// Đây là **ngoại lệ có chủ đích** với quy tắc "không log" của §6.5 mục 4: một
/// hệ thống có quyền tắt máy từ xa thì phải trả lời được câu "ai đã tắt máy tôi
/// lúc 3 giờ sáng". Ghi phiên nào gọi, không ghi nội dung nhạy cảm nào khác.
/// </summary>
public sealed class DeviceCommandAudit
{
    public int Id { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Phiên đã gọi lệnh — truy ngược được về thiết bị nào bấm nút.</summary>
    public Guid? SessionId { get; set; }

    public required Guid DeviceId { get; set; }

    /// <summary>Chép lại tên máy tại thời điểm gọi, để nhật ký còn đọc được sau khi máy bị xoá.</summary>
    public required string DeviceHostname { get; set; }

    public required PowerAction Action { get; set; }

    public required bool Succeeded { get; set; }

    /// <summary>Lý do thất bại, dạng mã ngắn — không phải stack trace (§6.5 mục 7).</summary>
    public string? FailureReason { get; set; }
}

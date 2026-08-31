namespace Hub.Core.Devices;

/// <summary>Lưu trữ sổ đăng ký thiết bị và nhật ký lệnh. Hiện thực EF ở Hub.Data.</summary>
public interface IDeviceStore
{
    Task<RegisteredDevice?> GetAsync(Guid deviceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegisteredDevice>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Tìm theo hostname để agent đăng ký lại không tạo bản ghi trùng.</summary>
    Task<RegisteredDevice?> FindByHostnameAsync(
        string hostname,
        CancellationToken cancellationToken = default);

    Task AddAsync(RegisteredDevice device, CancellationToken cancellationToken = default);

    Task UpdateAsync(RegisteredDevice device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá hẳn thiết bị khỏi sổ. Trả false nếu không tìm thấy.
    ///
    /// KHÔNG xoá nhật ký kiểm toán của nó (§5a điều 7): nhật ký đã chép sẵn
    /// hostname nên vẫn đọc được, và "ai đã tắt máy tôi lúc 3 giờ sáng" phải trả
    /// lời được kể cả sau khi thiết bị bị gỡ.
    /// </summary>
    Task<bool> DeleteAsync(Guid deviceId, CancellationToken cancellationToken = default);

    Task RecordCommandAsync(
        DeviceCommandAudit audit,
        CancellationToken cancellationToken = default);

    /// <summary>Nhật ký lệnh gần nhất, mới trước.</summary>
    Task<IReadOnlyList<DeviceCommandAudit>> GetRecentCommandsAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

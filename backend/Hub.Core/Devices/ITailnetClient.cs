using Hub.Core.Results;

namespace Hub.Core.Devices;

/// <summary>
/// Đọc danh sách thiết bị từ Tailscale. Hiện thực gọi HTTP nằm ở Hub.Api —
/// Core không biết gì về HttpClient (§3, luật phụ thuộc).
/// </summary>
public interface ITailnetClient
{
    Task<Result<IReadOnlyList<TailnetDevice>>> GetDevicesAsync(
        CancellationToken cancellationToken = default);
}

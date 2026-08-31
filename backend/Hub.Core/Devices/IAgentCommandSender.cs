using Hub.Core.Results;

namespace Hub.Core.Devices;

/// <summary>
/// Gửi lệnh tới agent chạy trên máy đích. Hiện thực gọi HTTP nằm ở Hub.Api.
///
/// Backend KHÔNG tự thực thi lệnh Windows — nó chỉ ra lệnh cho agent (§3.3).
/// Ranh giới này là thứ cho phép backend chuyển sang NAS mà không viết lại.
/// </summary>
public interface IAgentCommandSender
{
    Task<Result> SendAsync(
        RegisteredDevice device,
        PowerAction action,
        CancellationToken cancellationToken = default);
}

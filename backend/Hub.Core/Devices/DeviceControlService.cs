// ĐÃ THAY THẾ — 2026-08-31.
//
// Năng lực 6 chuyển sang dùng MeshCentral (§2.3: tái sử dụng, đừng phát minh
// lại). Mã dưới đây là bản tự viết trước đó: nó CHẠY ĐƯỢC và có test, nhưng
// không còn là đường chính.
//
// Vì sao chuyển: MeshCentral cho sẵn Wake-on-LAN (phần khó nhất, chưa làm
// được), agent đóng gói cho mọi hệ điều hành, remote desktop, và giao diện
// mobile — đều là thứ tự làm sẽ tốn nhiều công mà không tốt hơn.
//
// Giữ lại tạm thời để còn quay về được nếu MeshCentral không hợp. Đừng xây
// thêm trên mã này; xem docs/meshcentral-setup.md.

using Hub.Core.Abstractions;
using Hub.Core.Results;
using Microsoft.Extensions.Logging;

namespace Hub.Core.Devices;

/// <summary>
/// Điều khiển nguồn máy từ xa (§5a — năng lực 6).
///
/// Mọi quy tắc an toàn của §5a nằm ở đây, không nằm ở tầng endpoint: endpoint
/// có thể bị thêm mới hoặc gọi từ chỗ khác, còn service này là chỗ duy nhất
/// dẫn tới agent. Đặt kiểm tra ở đây thì không có đường vòng.
/// </summary>
public sealed class DeviceControlService(
    IDeviceStore store,
    IAgentCommandSender sender,
    IClock clock,
    ILogger<DeviceControlService> logger)
{
    /// <summary>
    /// Thực thi một hành động điều khiển nguồn.
    ///
    /// <paramref name="sessionId"/> chỉ dùng cho nhật ký kiểm toán (§5a điều 7).
    /// </summary>
    public async Task<Result> ExecuteAsync(
        Guid deviceId,
        PowerAction action,
        Guid? sessionId,
        CancellationToken cancellationToken = default)
    {
        var device = await store.GetAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return Result.Failure(ResultError.Validation("Không tìm thấy thiết bị."));
        }

        var guard = CheckGuards(device, action);
        if (guard.IsFailure)
        {
            // Lệnh bị chặn cũng phải vào nhật ký: một chuỗi lệnh bị từ chối là
            // dấu hiệu đáng chú ý, không phải chuyện vô hại để bỏ qua.
            await RecordAsync(device, action, sessionId, guard.Error!.Value, cancellationToken);
            return guard;
        }

        var result = await sender.SendAsync(device, action, cancellationToken);

        await RecordAsync(
            device,
            action,
            sessionId,
            result.IsSuccess ? null : result.Error,
            cancellationToken);

        if (result.IsSuccess)
        {
            logger.LogWarning(
                "Đã gửi lệnh {Action} tới {Hostname} (phiên {SessionId}).",
                action, device.Hostname, sessionId);
        }

        return result;
    }

    /// <summary>Các điều kiện bắt buộc của §5a, kiểm trước khi chạm tới agent.</summary>
    private static Result CheckGuards(RegisteredDevice device, PowerAction action)
    {
        // §5a: thiết bị mới phải được duyệt thủ công một lần trước khi nhận lệnh.
        if (!device.IsApproved)
        {
            return Result.Failure(ResultError.Validation(
                "Thiết bị chưa được duyệt. Vào màn hình thiết bị để duyệt trước."));
        }

        // §5a điều 5: không tắt được máy đang chạy backend qua giao diện. Tự tắt
        // server đang phục vụ chính request này là hành vi khó hiểu — và nó cắt
        // luôn đường vào hệ thống.
        //
        // Lock thì vẫn cho: khoá màn hình không làm backend ngừng phục vụ.
        if (device.IsBackendHost && action is not PowerAction.Lock)
        {
            return Result.Failure(ResultError.Conflict(
                $"Không thể {Describe(action)} máy đang chạy hub ({device.Hostname}). " +
                "Làm trực tiếp trên máy đó nếu thực sự muốn."));
        }

        return Result.Success();
    }

    private async Task RecordAsync(
        RegisteredDevice device,
        PowerAction action,
        Guid? sessionId,
        ResultError? error,
        CancellationToken cancellationToken)
    {
        await store.RecordCommandAsync(
            new DeviceCommandAudit
            {
                RequestedAt = clock.UtcNow,
                SessionId = sessionId,
                DeviceId = device.Id,
                DeviceHostname = device.Hostname,
                Action = action,
                Succeeded = error is null,
                FailureReason = error?.Code
            },
            cancellationToken);
    }

    private static string Describe(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "tắt",
        PowerAction.Restart => "khởi động lại",
        PowerAction.Sleep => "cho ngủ",
        PowerAction.Lock => "khoá",
        _ => action.ToString()
    };
}

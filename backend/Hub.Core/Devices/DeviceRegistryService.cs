using System.Text.RegularExpressions;
using Hub.Core.Abstractions;
using Hub.Core.Results;
using Microsoft.Extensions.Logging;

namespace Hub.Core.Devices;

/// <summary>
/// Sổ đăng ký thiết bị (§5a). Agent tự đăng ký, người dùng duyệt thủ công.
/// </summary>
public sealed partial class DeviceRegistryService(
    IDeviceStore store,
    IClock clock,
    ILogger<DeviceRegistryService> logger)
{
    /// <summary>
    /// Agent báo danh. Gọi lúc khởi động và định kỳ sau đó.
    ///
    /// Thiết bị đã biết thì cập nhật thông tin; thiết bị mới thì tạo ở trạng
    /// thái **chờ duyệt** — §5a: không nhận lệnh ngay.
    /// </summary>
    public async Task<Result<RegisteredDevice>> RegisterAsync(
        DeviceRegistration registration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registration.Hostname))
        {
            return Result.Failure<RegisteredDevice>(
                ResultError.Validation("Thiếu tên máy."));
        }

        var macAddress = NormalizeMac(registration.MacAddress);

        // MAC sai định dạng thì từ chối luôn thay vì lưu rác: nó là thứ bắt buộc
        // để đánh thức sau này, và lúc máy đã tắt thì không hỏi lại được (§5a).
        if (registration.MacAddress is not null && macAddress is null)
        {
            return Result.Failure<RegisteredDevice>(
                ResultError.Validation("Địa chỉ MAC không hợp lệ."));
        }

        var now = clock.UtcNow;
        var existing = await store.FindByHostnameAsync(registration.Hostname, cancellationToken);

        if (existing is not null)
        {
            existing.OperatingSystem = registration.OperatingSystem;
            existing.TailnetAddress = registration.TailnetAddress;
            existing.LanLabel = registration.LanLabel;
            existing.LastSeenAt = now;

            if (registration.FromAgent)
            {
                existing.AgentLastSeenAt = now;
            }

            // Chỉ ghi đè MAC khi agent báo giá trị mới. Agent chạy trên Wi-Fi có
            // thể không đọc được MAC của card Ethernet — đừng xoá mất giá trị cũ.
            if (macAddress is not null)
            {
                existing.MacAddress = macAddress;
            }

            // KHÔNG đụng tới IsApproved: đăng ký lại không được tự nâng quyền.
            await store.UpdateAsync(existing, cancellationToken);
            return Result.Success(existing);
        }

        var device = new RegisteredDevice
        {
            Id = Guid.NewGuid(),
            Hostname = registration.Hostname,
            OperatingSystem = registration.OperatingSystem,
            TailnetAddress = registration.TailnetAddress,
            MacAddress = macAddress,
            LanLabel = registration.LanLabel,
            IsApproved = false,
            RegisteredAt = now,
            LastSeenAt = now,
            IsBackendHost = registration.IsBackendHost,
            AgentLastSeenAt = registration.FromAgent ? now : null
        };

        await store.AddAsync(device, cancellationToken);

        logger.LogWarning(
            "Thiết bị mới đăng ký: {Hostname}. Đang chờ duyệt thủ công.", device.Hostname);

        return Result.Success(device);
    }

    public Task<IReadOnlyList<RegisteredDevice>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => store.GetAllAsync(cancellationToken);

    /// <summary>Duyệt thiết bị — sau bước này nó mới nhận được lệnh (§5a).</summary>
    public async Task<Result> ApproveAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
        => await SetApprovalAsync(deviceId, approved: true, cancellationToken);

    /// <summary>Thu hồi duyệt — dùng khi nghi ngờ một máy không còn tin được.</summary>
    public async Task<Result> RevokeApprovalAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
        => await SetApprovalAsync(deviceId, approved: false, cancellationToken);

    /// <summary>
    /// Gỡ thiết bị khỏi sổ. Dùng khi máy không còn thuộc hệ thống.
    ///
    /// Máy đó cài lại agent thì sẽ đăng ký lại từ đầu — và lại ở trạng thái
    /// **chờ duyệt** (§5a), không tự động lấy lại quyền cũ.
    /// </summary>
    public async Task<Result> DeleteAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await store.GetAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return Result.Failure(ResultError.Validation("Không tìm thấy thiết bị."));
        }

        await store.DeleteAsync(deviceId, cancellationToken);

        logger.LogWarning("Đã gỡ thiết bị {Hostname} khỏi sổ đăng ký.", device.Hostname);
        return Result.Success();
    }

    private async Task<Result> SetApprovalAsync(
        Guid deviceId,
        bool approved,
        CancellationToken cancellationToken)
    {
        var device = await store.GetAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return Result.Failure(ResultError.Validation("Không tìm thấy thiết bị."));
        }

        device.IsApproved = approved;
        await store.UpdateAsync(device, cancellationToken);

        logger.LogWarning(
            "{Action} thiết bị {Hostname}.",
            approved ? "Đã duyệt" : "Đã thu hồi duyệt", device.Hostname);

        return Result.Success();
    }

    /// <summary>
    /// Chuẩn hoá MAC về dạng AA:BB:CC:DD:EE:FF. Chấp nhận cả dấu gạch ngang,
    /// dấu chấm, hoặc không phân cách — mỗi hệ điều hành in ra một kiểu.
    /// </summary>
    public static string? NormalizeMac(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var hex = NonHexPattern().Replace(raw, "");

        if (hex.Length != 12)
        {
            return null;
        }

        return string.Join(':', Enumerable.Range(0, 6)
            .Select(index => hex.Substring(index * 2, 2).ToUpperInvariant()));
    }

    [GeneratedRegex("[^0-9a-fA-F]")]
    private static partial Regex NonHexPattern();
}

/// <summary>Thông tin agent gửi lên khi đăng ký.</summary>
public sealed record DeviceRegistration
{
    public required string Hostname { get; init; }

    public required string OperatingSystem { get; init; }

    public string? TailnetAddress { get; init; }

    public string? MacAddress { get; init; }

    public string? LanLabel { get; init; }

    public bool IsBackendHost { get; init; }

    /// <summary>
    /// True khi chính agent báo danh; false khi đăng ký bằng script.
    ///
    /// Script chỉ ghi thiết bị vào sổ — nó KHÔNG nhận lệnh được. Phân biệt để
    /// báo lỗi đúng: "agent chưa chạy" khác hẳn "máy đã tắt".
    /// </summary>
    public bool FromAgent { get; init; }
}

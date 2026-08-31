using Hub.Core.Abstractions;
using Hub.Core.Devices;
using Hub.Core.Results;
using Microsoft.Extensions.Options;

namespace Hub.Api.Devices;

/// <summary>
/// Bọc <see cref="TailscaleClient"/> bằng một lớp cache ngắn.
///
/// Lý do: trang danh sách thiết bị sẽ được mở lại liên tục (và về sau còn poll
/// định kỳ). Gọi thẳng Tailscale mỗi lần là tự đâm vào giới hạn tần suất của
/// họ, trong khi dữ liệu hiện diện không cần chính xác tới từng giây.
///
/// Chỉ cache kết quả THÀNH CÔNG — cache cả lỗi sẽ giữ nguyên trạng thái hỏng
/// suốt thời gian cache, kể cả khi sự cố đã hết.
/// </summary>
public sealed class CachedTailnetClient(
    TailscaleClient inner,
    IClock clock,
    IOptions<TailscaleOptions> options) : ITailnetClient
{
    private readonly TailscaleOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IReadOnlyList<TailnetDevice>? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<Result<IReadOnlyList<TailnetDevice>>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        if (TryGetFresh(out var fresh))
        {
            return Result.Success(fresh!);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Request khác có thể đã làm mới xong trong lúc ta chờ khoá.
            if (TryGetFresh(out fresh))
            {
                return Result.Success(fresh!);
            }

            var result = await inner.GetDevicesAsync(cancellationToken);

            if (result.IsSuccess)
            {
                _cached = result.Value;
                _cachedAt = clock.UtcNow;
            }

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool TryGetFresh(out IReadOnlyList<TailnetDevice>? devices)
    {
        devices = _cached;
        return devices is not null && clock.UtcNow - _cachedAt < _options.CacheDuration;
    }
}

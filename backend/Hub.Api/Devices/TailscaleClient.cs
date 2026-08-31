using System.Net;
using System.Text.Json.Serialization;
using Hub.Core.Abstractions;
using Hub.Core.Devices;
using Hub.Core.Results;
using Microsoft.Extensions.Options;

namespace Hub.Api.Devices;

/// <summary>
/// Hiện thực <see cref="ITailnetClient"/> gọi Tailscale API v2.
///
/// §2.3: hệ thống này là control plane — dùng lại Tailscale làm nguồn sự thật
/// về thiết bị, không tự dựng cơ chế phát hiện thiết bị.
/// </summary>
public sealed class TailscaleClient(
    IHttpClientFactory httpClientFactory,
    TailscaleTokenProvider tokenProvider,
    IOptions<TailscaleOptions> options,
    IClock clock,
    ILogger<TailscaleClient> logger) : ITailnetClient
{
    /// <summary>Tên client đăng ký với IHttpClientFactory (§3: không new HttpClient).</summary>
    public const string HttpClientName = "tailscale";

    private readonly TailscaleOptions _options = options.Value;

    public async Task<Result<IReadOnlyList<TailnetDevice>>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            // Thất bại rõ ràng kèm hướng dẫn, thay vì trả danh sách rỗng khiến
            // người dùng tưởng không có thiết bị nào.
            return Result.Failure<IReadOnlyList<TailnetDevice>>(new ResultError(
                "tailscale_not_configured",
                "Chưa cấu hình Tailscale API. Cần khai Tailscale:ClientId và " +
                "Tailscale:ClientSecret (xem docs/tailscale-setup.md)."));
        }

        var accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (accessToken is null)
        {
            return Result.Failure<IReadOnlyList<TailnetDevice>>(new ResultError(
                "tailscale_auth_failed",
                "Không lấy được token Tailscale. Kiểm tra client ID và secret."));
        }

        var response = await SendAsync(accessToken, cancellationToken);

        // Token có thể bị thu hồi trước hạn. Thử lại đúng một lần với token mới,
        // rồi thôi — lặp vô hạn khi credential sai là cách tự làm mình bị chặn.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Tailscale trả 401, thử lấy token mới.");
            tokenProvider.Invalidate();
            response.Dispose();

            accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken);
            if (accessToken is null)
            {
                return Result.Failure<IReadOnlyList<TailnetDevice>>(new ResultError(
                    "tailscale_auth_failed", "Không lấy được token Tailscale."));
            }

            response = await SendAsync(accessToken, cancellationToken);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Tailscale API trả HTTP {StatusCode} khi liệt kê thiết bị.",
                    (int)response.StatusCode);

                return Result.Failure<IReadOnlyList<TailnetDevice>>(new ResultError(
                    "tailscale_unavailable",
                    "Không đọc được danh sách thiết bị từ Tailscale."));
            }

            var payload = await response.Content
                .ReadFromJsonAsync<DeviceListResponse>(cancellationToken);

            if (payload?.Devices is null)
            {
                return Result.Failure<IReadOnlyList<TailnetDevice>>(new ResultError(
                    "tailscale_unavailable", "Tailscale trả về nội dung không đọc được."));
            }

            var now = clock.UtcNow;
            IReadOnlyList<TailnetDevice> devices = payload.Devices
                .Select(device => Map(device, now))
                .OrderByDescending(device => device.IsOnline)
                .ThenBy(device => device.Hostname, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Result.Success(devices);
        }
    }

    private Task<HttpResponseMessage> SendAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        // Tailnet "-" nghĩa là tailnet mặc định của credential đang dùng.
        var url = $"https://api.tailscale.com/api/v2/tailnet/{_options.Tailnet}/devices";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = TailscaleTokenProvider.CreateHeader(accessToken);

        return client.SendAsync(request, cancellationToken);
    }

    private TailnetDevice Map(TailscaleDevice source, DateTimeOffset now)
    {
        // API danh sách thiết bị KHÔNG có trường "online" — chỉ có lastSeen.
        // Trạng thái hiện diện vì thế là suy luận theo ngưỡng, không phải sự thật
        // do Tailscale khẳng định. Xem chú thích ở TailnetDevice.IsOnline.
        var isOnline = source.LastSeen is not null
            && now - source.LastSeen.Value <= _options.OnlineThreshold;

        return new TailnetDevice
        {
            // nodeId là định danh Tailscale khuyến nghị; id kiểu số là kiểu cũ.
            Id = string.IsNullOrWhiteSpace(source.NodeId) ? source.Id ?? "" : source.NodeId,
            Hostname = source.Hostname ?? "(không rõ)",
            Name = source.Name ?? "",
            OperatingSystem = source.Os ?? "",
            TailnetAddress = FindIPv4(source.Addresses),
            LastSeen = source.LastSeen,
            IsOnline = isOnline,
            Authorized = source.Authorized,
            IsExternal = source.IsExternal,
            ClientVersion = source.ClientVersion,
            UpdateAvailable = source.UpdateAvailable
        };
    }

    /// <summary>
    /// Lấy địa chỉ IPv4 (100.x.y.z). Danh sách addresses chứa cả IPv6, mà phần
    /// còn lại của hệ thống làm việc với IPv4 tailnet (§4).
    /// </summary>
    private static string? FindIPv4(IReadOnlyList<string>? addresses)
    {
        if (addresses is null)
        {
            return null;
        }

        return addresses.FirstOrDefault(address =>
            IPAddress.TryParse(address, out var parsed)
            && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    }

    private sealed record DeviceListResponse
    {
        [JsonPropertyName("devices")]
        public List<TailscaleDevice>? Devices { get; init; }
    }

    /// <summary>Ánh xạ đúng theo tài liệu API v2 của Tailscale.</summary>
    private sealed record TailscaleDevice
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("nodeId")]
        public string? NodeId { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; init; }

        [JsonPropertyName("os")]
        public string? Os { get; init; }

        [JsonPropertyName("addresses")]
        public List<string>? Addresses { get; init; }

        [JsonPropertyName("lastSeen")]
        public DateTimeOffset? LastSeen { get; init; }

        [JsonPropertyName("authorized")]
        public bool Authorized { get; init; }

        [JsonPropertyName("isExternal")]
        public bool IsExternal { get; init; }

        [JsonPropertyName("clientVersion")]
        public string? ClientVersion { get; init; }

        [JsonPropertyName("updateAvailable")]
        public bool UpdateAvailable { get; init; }
    }
}

using System.Net;
using System.Text;
using Hub.Api.Devices;
using Hub.Core.Abstractions;
using Hub.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hub.Api.Tests;

/// <summary>
/// Test cho client Tailscale. Chặn HTTP bằng handler giả — không gọi ra mạng
/// thật, nên test chạy được offline và không phụ thuộc tài khoản.
/// </summary>
public sealed class TailscaleClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// JSON đúng hình dạng Tailscale API v2 trả về. Giữ nguyên tên trường thật
    /// để test bắt được lỗi nếu ai đó đổi JsonPropertyName.
    /// </summary>
    private const string DeviceListJson = """
    {
      "devices": [
        {
          "id": "393735751060",
          "nodeId": "nPC123CNTRL",
          "name": "pc.tailnet-example.ts.net",
          "hostname": "pc",
          "os": "windows",
          "addresses": ["100.100.100.100", "fd7a:115c:a1e0:ac82:4843:ca90:697d:c36e"],
          "lastSeen": "2026-08-29T11:59:00Z",
          "authorized": true,
          "isExternal": false,
          "clientVersion": "1.76.0",
          "updateAvailable": false
        },
        {
          "id": "393735751061",
          "nodeId": "nPhone456CNTRL",
          "name": "iphone.tailnet-example.ts.net",
          "hostname": "iphone",
          "os": "iOS",
          "addresses": ["100.64.10.20"],
          "lastSeen": "2026-08-29T08:00:00Z",
          "authorized": true,
          "isExternal": false,
          "clientVersion": "1.76.0",
          "updateAvailable": true
        }
      ]
    }
    """;

    [Fact]
    public async Task GetDevices_ChuaCauHinh_BaoLoiRoRang()
    {
        var client = CreateClient(new TailscaleOptions(), _ => throw new InvalidOperationException(
            "Không được gọi HTTP khi chưa cấu hình."));

        var result = await client.GetDevicesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("tailscale_not_configured", result.Error!.Value.Code);
    }

    [Fact]
    public async Task GetDevices_AnhXaDungCacTruong()
    {
        var client = CreateClient(ConfiguredOptions(), HandleWithToken);

        var result = await client.GetDevicesAsync();

        Assert.True(result.IsSuccess);
        var pc = result.Value.Single(device => device.Hostname == "pc");

        // nodeId được ưu tiên hơn id kiểu số.
        Assert.Equal("nPC123CNTRL", pc.Id);
        Assert.Equal("pc.tailnet-example.ts.net", pc.Name);
        Assert.Equal("windows", pc.OperatingSystem);
        Assert.True(pc.Authorized);
        Assert.False(pc.UpdateAvailable);
    }

    /// <summary>Danh sách addresses có cả IPv6; phần còn lại của hệ thống dùng IPv4 (§4).</summary>
    [Fact]
    public async Task GetDevices_ChonDiaChiIPv4()
    {
        var client = CreateClient(ConfiguredOptions(), HandleWithToken);

        var result = await client.GetDevicesAsync();

        Assert.Equal("100.100.100.100",
            result.Value.Single(device => device.Hostname == "pc").TailnetAddress);
    }

    /// <summary>
    /// Tailscale KHÔNG trả trường "online" — trạng thái suy từ lastSeen. Test
    /// này chốt hành vi đó để không ai tưởng nhầm là dữ liệu gốc.
    /// </summary>
    [Fact]
    public async Task GetDevices_SuyTrangThaiOnlineTuLastSeen()
    {
        var client = CreateClient(ConfiguredOptions(), HandleWithToken);

        var result = await client.GetDevicesAsync();

        // pc: lastSeen 1 phút trước, ngưỡng 5 phút -> online.
        Assert.True(result.Value.Single(device => device.Hostname == "pc").IsOnline);

        // iphone: lastSeen 4 tiếng trước -> offline.
        Assert.False(result.Value.Single(device => device.Hostname == "iphone").IsOnline);
    }

    [Fact]
    public async Task GetDevices_MayOnlineXepTruoc()
    {
        var client = CreateClient(ConfiguredOptions(), HandleWithToken);

        var result = await client.GetDevicesAsync();

        Assert.Equal("pc", result.Value[0].Hostname);
    }

    [Fact]
    public async Task GetDevices_TokenHong_BaoLoiXacThuc()
    {
        var client = CreateClient(ConfiguredOptions(), request =>
            IsTokenRequest(request)
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : throw new InvalidOperationException("Không nên gọi API khi chưa có token."));

        var result = await client.GetDevicesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("tailscale_auth_failed", result.Error!.Value.Code);
    }

    /// <summary>
    /// Token có thể bị thu hồi trước hạn. Gặp 401 thì lấy token mới và thử lại
    /// đúng một lần.
    /// </summary>
    [Fact]
    public async Task GetDevices_Gap401_LayTokenMoiVaThuLai()
    {
        var deviceCallCount = 0;

        var client = CreateClient(ConfiguredOptions(), request =>
        {
            if (IsTokenRequest(request))
            {
                return JsonResponse("""{"access_token":"tok","expires_in":3600}""");
            }

            deviceCallCount++;

            // Lần đầu giả vờ token đã bị thu hồi.
            return deviceCallCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : JsonResponse(DeviceListJson);
        });

        var result = await client.GetDevicesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, deviceCallCount);
    }

    [Fact]
    public async Task GetDevices_ApiLoi_KhongLamSapUngDung()
    {
        var client = CreateClient(ConfiguredOptions(), request =>
            IsTokenRequest(request)
                ? JsonResponse("""{"access_token":"tok","expires_in":3600}""")
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await client.GetDevicesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("tailscale_unavailable", result.Error!.Value.Code);
    }

    [Fact]
    public async Task GetDevices_ThieuLastSeen_CoiLaOffline()
    {
        const string json = """
        {"devices":[{"nodeId":"n1","hostname":"máy-lạ","os":"linux","addresses":["100.1.2.3"]}]}
        """;

        var client = CreateClient(ConfiguredOptions(), request =>
            IsTokenRequest(request)
                ? JsonResponse("""{"access_token":"tok","expires_in":3600}""")
                : JsonResponse(json));

        var result = await client.GetDevicesAsync();

        Assert.False(Assert.Single(result.Value).IsOnline);
    }

    private static TailscaleOptions ConfiguredOptions() => new()
    {
        ClientId = "test-client",
        ClientSecret = "test-secret",
        Tailnet = "-"
    };

    private static bool IsTokenRequest(HttpRequestMessage request)
        => request.RequestUri!.AbsolutePath.EndsWith("/oauth/token", StringComparison.Ordinal);

    private static HttpResponseMessage HandleWithToken(HttpRequestMessage request)
        => IsTokenRequest(request)
            ? JsonResponse("""{"access_token":"tok","expires_in":3600}""")
            : JsonResponse(DeviceListJson);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static TailscaleClient CreateClient(
        TailscaleOptions options,
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var factory = new StubHttpClientFactory(handler);
        var clock = new FixedClock(Now);
        var wrapped = Options.Create(options);

        var tokenProvider = new TailscaleTokenProvider(
            factory, wrapped, clock, NullLogger<TailscaleTokenProvider>.Instance);

        return new TailscaleClient(
            factory, tokenProvider, wrapped, clock, NullLogger<TailscaleClient>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class StubHttpClientFactory(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(handler));
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}

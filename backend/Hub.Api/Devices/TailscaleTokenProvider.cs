using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Hub.Core.Abstractions;
using Hub.Core.Devices;
using Microsoft.Extensions.Options;

namespace Hub.Api.Devices;

/// <summary>
/// Lấy và giữ access token của Tailscale (OAuth 2.0 client credentials).
///
/// Token hết hạn sau một giờ, nên phải tự làm mới. Đăng ký dạng singleton để
/// token dùng chung cho mọi request thay vì mỗi request xin một cái mới.
/// </summary>
public sealed class TailscaleTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TailscaleOptions> options,
    IClock clock,
    ILogger<TailscaleTokenProvider> logger)
{
    private const string TokenEndpoint = "https://api.tailscale.com/api/v2/oauth/token";

    /// <summary>
    /// Xin token mới sớm hơn hạn một chút, để không có request nào lỡ dùng
    /// đúng lúc token vừa hết hiệu lực.
    /// </summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

    // Chặn nhiều request cùng gọi token endpoint khi token vừa hết hạn —
    // không có khoá thì mỗi request đang chờ sẽ xin một token riêng.
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly TailscaleOptions _options = options.Value;

    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return null;
        }

        if (IsTokenUsable())
        {
            return _accessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Kiểm lại sau khi giành được khoá: request khác có thể đã lấy xong.
            if (IsTokenUsable())
            {
                return _accessToken;
            }

            return await FetchTokenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Buộc lấy token mới ở lần gọi sau — dùng khi API trả 401.</summary>
    public void Invalidate()
    {
        _accessToken = null;
        _expiresAt = default;
    }

    private bool IsTokenUsable()
        => _accessToken is not null && clock.UtcNow < _expiresAt - ExpiryMargin;

    private async Task<string?> FetchTokenAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(TailscaleClient.HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId!,
                ["client_secret"] = _options.ClientSecret!
            })
        };

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // §6.5 mục 4: không log secret. Chỉ ghi mã lỗi, không ghi body —
            // body của lỗi OAuth có thể chứa lại tham số đã gửi.
            logger.LogError(
                "Lấy token Tailscale thất bại: HTTP {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            logger.LogError("Token endpoint trả về nội dung không đọc được.");
            return null;
        }

        _accessToken = token.AccessToken;
        _expiresAt = clock.UtcNow.AddSeconds(token.ExpiresInSeconds);

        logger.LogInformation("Đã lấy token Tailscale, hết hạn lúc {ExpiresAt}.", _expiresAt);
        return _accessToken;
    }

    public static AuthenticationHeaderValue CreateHeader(string accessToken)
        => new("Bearer", accessToken);

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; init; }
    }
}

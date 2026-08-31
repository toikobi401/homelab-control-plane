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

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hub.Core.Devices;
using Hub.Core.Results;
using Microsoft.Extensions.Options;

namespace Hub.Api.Devices;

/// <summary>
/// Gửi lệnh điều khiển tới agent qua HTTP trên tailnet.
///
/// §5a điều 2: chỉ POST, body JSON — không truyền lệnh qua query string vì
/// query string bị ghi vào log server, lịch sử trình duyệt, và referer.
/// </summary>
public sealed class HttpAgentCommandSender(
    IHttpClientFactory httpClientFactory,
    IOptions<AgentOptions> options,
    ILogger<HttpAgentCommandSender> logger) : IAgentCommandSender
{
    public const string HttpClientName = "hub-agent";

    private readonly AgentOptions _options = options.Value;

    public async Task<Result> SendAsync(
        RegisteredDevice device,
        PowerAction action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.TailnetAddress))
        {
            return Result.Failure(ResultError.Validation(
                $"Chưa biết địa chỉ tailnet của {device.Hostname}."));
        }

        // §6.4: agent phải xác thực được backend. Không có shared secret thì bất
        // kỳ ai trong tailnet cũng gọi thẳng agent và tắt máy được — tailnet là
        // lớp phòng thủ thứ nhất, không phải duy nhất.
        if (string.IsNullOrWhiteSpace(_options.SharedSecret))
        {
            logger.LogError("Chưa cấu hình Agent:SharedSecret — từ chối gửi lệnh.");
            return Result.Failure(new ResultError(
                "agent_not_configured",
                "Chưa cấu hình khoá chung với agent. Xem docs/agent-setup.md."));
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"http://{device.TailnetAddress}:{_options.Port}/agent/power";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _options.SharedSecret);

        // Gửi enum dạng chuỗi có kiểu rõ ràng, không phải chuỗi lệnh tuỳ ý (§5a điều 1).
        //
        // Dựng JSON tường minh thay vì JsonContent.Create(record): record khai
        // bên trong class này bị serialize thành `{}` — agent nhận body rỗng rồi
        // từ chối với "hành động không hợp lệ". Đã gặp thật lúc kiểm chứng, và
        // body rỗng là loại lỗi im lặng khó truy nhất.
        var payload = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["action"] = action.ToString() });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            logger.LogError(
                "Agent {Hostname} trả HTTP {StatusCode} cho lệnh {Action}.",
                device.Hostname, (int)response.StatusCode, action);

            return Result.Failure(new ResultError(
                "agent_rejected",
                $"Agent trên {device.Hostname} từ chối lệnh."));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // §5a điều 6: agent không phản hồi thì báo lỗi rõ ràng, không treo
            // giao diện. Đây là timeout của HttpClient, không phải người dùng huỷ.
            logger.LogError("Agent {Hostname} không phản hồi trong thời gian chờ.", device.Hostname);

            return Result.Failure(new ResultError("agent_timeout", DescribeSilence(device)));
        }
        catch (HttpRequestException ex)
        {
            // Không đưa chi tiết exception ra ngoài (§6.5 mục 7) — chỉ vào log.
            logger.LogError(ex, "Không kết nối được agent {Hostname}.", device.Hostname);

            return Result.Failure(new ResultError("agent_unreachable", DescribeSilence(device)));
        }
    }

    /// <summary>
    /// Giải thích vì sao agent im lặng.
    ///
    /// Phân biệt hai tình huống khác hẳn nhau: agent CHƯA BAO GIỜ chạy (thiết bị
    /// đăng ký bằng script nhưng chưa cài agent) và agent TỪNG chạy rồi im (máy
    /// tắt, mất mạng). Đoán sai thì người dùng đi tìm nhầm chỗ — đã gặp thật:
    /// máy đang bật, đang được điều khiển từ xa, mà hệ thống báo "có thể đã tắt".
    /// </summary>
    private static string DescribeSilence(RegisteredDevice device)
    {
        if (device.AgentLastSeenAt is null)
        {
            return $"{device.Hostname} chưa từng chạy agent. Thiết bị đã có trong sổ " +
                "đăng ký nhưng chưa cài agent, nên không nhận được lệnh. " +
                "Xem docs/agent-setup.md.";
        }

        return $"{device.Hostname} không phản hồi. Máy có thể đã tắt hoặc mất mạng " +
            $"(agent báo danh lần cuối lúc {device.AgentLastSeenAt:HH:mm dd/MM}).";
    }

}

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Cổng agent lắng nghe trên mỗi máy.</summary>
    public int Port { get; set; } = 5199;

    /// <summary>
    /// Khoá chung giữa backend và agent. §6.5 mục 1: KHÔNG để trong source —
    /// dùng User Secrets lúc dev, biến môi trường lúc chạy thật.
    /// </summary>
    public string? SharedSecret { get; set; }

    /// <summary>
    /// §5a điều 6: agent không phản hồi trong vài giây thì báo lỗi.
    /// Để ngắn — người dùng đang đứng chờ trước màn hình điện thoại.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

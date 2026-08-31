using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Hub.Agent;

/// <summary>
/// Agent tự báo danh với hub (§5a, sổ đăng ký thiết bị).
///
/// Báo những thứ Tailscale KHÔNG biết: địa chỉ MAC (bắt buộc để đánh thức) và
/// nhãn LAN (để biết máy nào đánh thức được máy nào). §5a nói rõ MAC phải ghi
/// lại lúc máy còn online — lúc đã tắt thì không hỏi được nữa.
/// </summary>
public sealed class RegistrationWorker(
    IHttpClientFactory httpClientFactory,
    IOptions<AgentSettings> options,
    ILogger<RegistrationWorker> logger) : BackgroundService
{
    private readonly AgentSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.HubUrl)
            || string.IsNullOrWhiteSpace(_settings.SharedSecret))
        {
            // Thất bại rõ ràng thay vì chạy im lặng mà không bao giờ đăng ký được.
            logger.LogError(
                "Chưa cấu hình Agent:HubUrl hoặc Agent:SharedSecret — agent sẽ không báo danh. " +
                "Xem docs/agent-setup.md.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RegisterAsync(stoppingToken);

            try
            {
                await Task.Delay(_settings.HeartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("hub");

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{_settings.HubUrl!.TrimEnd('/')}/api/devices/register");

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", _settings.SharedSecret);

            request.Content = JsonContent.Create(new
            {
                Hostname = Environment.MachineName,
                OperatingSystem = GetOperatingSystemName(),
                MacAddress = FindMacAddress(),
                LanLabel = FindLanLabel(),
                _settings.IsBackendHost,
                FromAgent = true
            });

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogDebug("Đã báo danh với hub.");
            }
            else
            {
                logger.LogWarning(
                    "Hub từ chối đăng ký: HTTP {StatusCode}.", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hub tắt là chuyện bình thường — ghi cảnh báo rồi thử lại lần sau,
            // không để agent chết theo.
            logger.LogWarning("Không liên lạc được hub: {Message}", ex.Message);
        }
    }

    private static string GetOperatingSystemName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return OperatingSystem.IsMacOS() ? "macOS" : "unknown";
    }

    /// <summary>
    /// MAC của card mạng vật lý đang hoạt động.
    ///
    /// Bỏ qua loopback, tunnel, và card ảo — đánh thức chỉ hoạt động với card
    /// vật lý thật. Ưu tiên Ethernet: WoL qua Wi-Fi phụ thuộc nhiều vào driver
    /// và thường không chạy (§5a.1).
    /// </summary>
    private static string? FindMacAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(nic => nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                and not NetworkInterfaceType.Tunnel)
            .Where(nic => !nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)
                && !nic.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
                && !nic.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                && !nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var preferred = candidates
            .FirstOrDefault(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            ?? candidates.FirstOrDefault();

        var mac = preferred?.GetPhysicalAddress().GetAddressBytes();

        return mac is { Length: 6 }
            ? string.Join(':', mac.Select(part => part.ToString("X2")))
            : null;
    }

    /// <summary>
    /// Nhãn LAN suy từ subnet của card mạng vật lý — máy cùng subnet thì đánh
    /// thức được cho nhau (§5a.1: magic packet là broadcast tầng 2).
    ///
    /// Bỏ qua dải tailnet 100.64.0.0/10: mọi máy đều chung dải đó nhưng chúng
    /// KHÔNG cùng LAN vật lý, dùng nó làm nhãn sẽ kết luận sai.
    /// </summary>
    private static string? FindLanLabel()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(nic => nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                and not NetworkInterfaceType.Tunnel))
        {
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var octets = address.Address.GetAddressBytes();

                // Dải CGNAT của Tailscale — không phải LAN vật lý.
                if (octets[0] == 100 && octets[1] >= 64 && octets[1] <= 127)
                {
                    continue;
                }

                if (!IsPrivate(octets))
                {
                    continue;
                }

                var prefix = address.PrefixLength;
                var network = GetNetworkAddress(octets, prefix);
                return $"{network}/{prefix}";
            }
        }

        return null;
    }

    private static bool IsPrivate(byte[] octets)
        => octets[0] == 10
        || (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
        || (octets[0] == 192 && octets[1] == 168);

    private static string GetNetworkAddress(byte[] octets, int prefixLength)
    {
        var masked = new byte[4];
        for (var index = 0; index < 4; index++)
        {
            var bits = Math.Clamp(prefixLength - (index * 8), 0, 8);
            var mask = bits == 0 ? 0 : (byte)(0xFF << (8 - bits));
            masked[index] = (byte)(octets[index] & mask);
        }

        return new IPAddress(masked).ToString();
    }
}

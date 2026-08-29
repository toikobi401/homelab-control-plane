using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Hub.Api.Hosting;

/// <summary>
/// Cách backend chọn địa chỉ để lắng nghe. Xem CONTEXT.md §4.
/// </summary>
public enum BindMode
{
    /// <summary>Chỉ localhost. Dùng khi phát triển trên máy.</summary>
    Localhost,

    /// <summary>
    /// Tự dò card mạng Tailscale và bind đúng địa chỉ tailnet. Chạy thẳng trên
    /// máy (không container). Không tìm thấy thì thất bại rõ ràng.
    /// </summary>
    Tailnet,

    /// <summary>
    /// Bind mọi địa chỉ — CHỈ hợp lệ bên trong container.
    ///
    /// Nghe thì trái §4, nhưng không phải: network namespace của container là
    /// riêng, nên 0.0.0.0 ở đây là "mọi địa chỉ CỦA CONTAINER", không phải mọi
    /// card mạng của máy thật. Ranh giới bảo vệ chuyển ra cổng publish của
    /// Docker — compose.yaml bắt buộc publish lên đúng IP tailnet:
    ///
    ///     ports: ["${HUB_TAILNET_IP}:5000:8080"]
    ///
    /// Publish kiểu "5000:8080" (thiếu IP) là phơi ra Wi-Fi nhà — đúng thứ §4 cấm.
    /// </summary>
    Container
}

public static class NetworkBinding
{
    public const string ModeKey = "HUB_BIND_MODE";
    private const int ContainerPort = 8080;
    private const int TailnetPort = 5000;

    public static BindMode ResolveMode(IConfiguration configuration)
    {
        var configured = configuration[ModeKey];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Enum.TryParse<BindMode>(configured, ignoreCase: true, out var parsed))
            {
                throw new InvalidOperationException(
                    $"{ModeKey}='{configured}' không hợp lệ. Giá trị cho phép: " +
                    $"{string.Join(", ", Enum.GetNames<BindMode>())}.");
            }

            return parsed;
        }

        // Không cấu hình gì mà đang chạy trong container thì chọn Container —
        // để quên biến môi trường không biến thành lỗi khó hiểu lúc khởi động.
        return IsRunningInContainer() ? BindMode.Container : BindMode.Localhost;
    }

    public static void Apply(WebApplicationBuilder builder, BindMode mode)
    {
        switch (mode)
        {
            case BindMode.Localhost:
                // Dùng applicationUrl của launchSettings — không ghi đè.
                break;

            case BindMode.Container:
                builder.WebHost.ConfigureKestrel(kestrel =>
                    kestrel.ListenAnyIP(ContainerPort));
                break;

            case BindMode.Tailnet:
                var address = FindTailnetAddress()
                    ?? throw new InvalidOperationException(
                        "Không tìm thấy địa chỉ tailnet trên card mạng Tailscale. Kiểm tra " +
                        "Tailscale đã chạy chưa: `tailscale status`. Xem CONTEXT.md §4.");

                builder.WebHost.ConfigureKestrel(kestrel =>
                    kestrel.Listen(address, TailnetPort));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Chế độ bind lạ.");
        }
    }

    /// <summary>
    /// Chạy trong container thì bind 0.0.0.0 mới an toàn. Nếu biến này báo sai
    /// (chạy thẳng trên máy mà tưởng là container), ta sẽ phơi ra mạng nhà — nên
    /// chỉ tin biến chính thức do runtime đặt, không tự đoán.
    /// </summary>
    private static bool IsRunningInContainer()
    {
        return Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
    }

    // Tailscale cấp địa chỉ trong dải CGNAT 100.64.0.0/10.
    //
    // Chỉ xét đúng card mạng của Tailscale, KHÔNG quét mọi card. Lý do: VPN khác
    // (Radmin VPN, một số CGNAT của ISP) cũng có thể cấp địa chỉ trong cùng dải
    // 100.64.0.0/10 — khớp theo dải thôi thì có ngày bind nhầm sang mạng khác.
    private static IPAddress? FindTailnetAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(IsTailscaleInterface)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(addr => addr.Address)
            .Where(addr => addr.AddressFamily == AddressFamily.InterNetwork)
            .FirstOrDefault(IsTailnetAddress);
    }

    private static bool IsTailscaleInterface(NetworkInterface nic)
    {
        return nic.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)
            || nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTailnetAddress(IPAddress address)
    {
        var octets = address.GetAddressBytes();
        return octets[0] == 100 && octets[1] >= 64 && octets[1] <= 127;
    }
}

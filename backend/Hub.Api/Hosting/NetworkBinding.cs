using System.Net;
using System.Security.Cryptography.X509Certificates;
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
    Container,

    /// <summary>
    /// Chỉ nghe trên loopback, để **Cloudflare Tunnel** chuyển tiếp vào.
    ///
    /// Dùng khi mở hệ thống ra Internet. `cloudflared` chạy trên chính máy này
    /// và gọi vào 127.0.0.1 — không cổng nào mở ra LAN hay Internet, kể cả khi
    /// firewall bị tắt nhầm.
    ///
    /// Đây là ngoại lệ có chủ đích với §1 ("không đưa hệ thống ra Internet") —
    /// xem nhật ký quyết định trong CONTEXT.md §11. Đi kèm là rate limit và
    /// header bảo mật, những thứ tailnet vốn che cho ta.
    /// </summary>
    Tunnel,

    /// <summary>
    /// Nghe trên địa chỉ LAN, để router chuyển tiếp (port forwarding) vào.
    ///
    /// **Chế độ hở nhất trong bốn chế độ.** Nó phơi hub ra cả Wi-Fi nhà lẫn
    /// Internet — không có Cloudflare làm lớp chắn như <see cref="Tunnel"/>,
    /// nên rate limit và header bảo mật là phòng thủ duy nhất.
    ///
    /// Chỉ nên dùng tạm khi chưa có tên miền. Có miền rồi thì chuyển sang
    /// Tunnel: không mở cổng nào trên router, và có chứng chỉ thật.
    ///
    /// Bind đúng card mạng vật lý, không phải 0.0.0.0 — cách này ít nhất giữ
    /// cho Radmin VPN và các card ảo không lộ theo.
    /// </summary>
    Lan
}

/// <summary>Chứng chỉ TLS kèm chuỗi trung gian gửi cho client.</summary>
internal sealed record TlsCertificate(X509Certificate2 Leaf, X509Certificate2Collection Chain);

public static class NetworkBinding
{
    public const string ModeKey = "HUB_BIND_MODE";
    private const int ContainerPort = 8080;

    /// <summary>
    /// Cùng cổng với profile localhost, để frontend và script không phải đổi
    /// địa chỉ khi chuyển giữa hai chế độ.
    /// </summary>
    private const int TailnetPort = 7189;

    /// <summary>Cổng loopback cho Cloudflare Tunnel chuyển tiếp vào.</summary>
    private const int TunnelPort = 7190;

    /// <summary>Cổng nghe trên LAN khi dùng port forwarding.</summary>
    private const int LanPort = 7189;

    /// <summary>Khoá cấu hình trỏ tới chứng chỉ `tailscale cert` cấp.</summary>
    public const string CertificateKey = "HUB_TLS_CERT";
    public const string CertificateKeyKey = "HUB_TLS_KEY";

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

            case BindMode.Lan:
                var lanAddress = FindLanAddress()
                    ?? throw new InvalidOperationException(
                        "Không tìm thấy địa chỉ LAN riêng tư trên card mạng vật lý. " +
                        "Kiểm tra máy có kết nối mạng nhà không.");

                var lanCertificate = LoadCertificate(builder.Configuration);

                builder.WebHost.ConfigureKestrel(kestrel =>
                    kestrel.Listen(lanAddress, LanPort, listen =>
                    {
                        // Cookie phiên đặt Secure (§6.3) nên HTTP trần sẽ không
                        // giữ được phiên — đăng nhập xong vẫn bị đẩy ra.
                        if (lanCertificate is not null)
                        {
                            listen.UseHttps(lanCertificate.Leaf, https =>
                                https.ServerCertificateChain = lanCertificate.Chain);
                        }
                        else
                        {
                            listen.UseHttps();
                        }
                    }));
                break;

            case BindMode.Tunnel:
                // HTTP trần trên loopback: TLS do Cloudflare lo ở biên, và gói
                // tin không rời khỏi máy này nên không có gì để nghe lén.
                // Dùng HTTPS ở đây chỉ thêm một chứng chỉ phải quản mà không
                // được gì.
                builder.WebHost.ConfigureKestrel(kestrel =>
                    kestrel.ListenLocalhost(TunnelPort));
                break;

            case BindMode.Tailnet:
                var address = FindTailnetAddress()
                    ?? throw new InvalidOperationException(
                        "Không tìm thấy địa chỉ tailnet trên card mạng Tailscale. Kiểm tra " +
                        "Tailscale đã chạy chưa: `tailscale status`. Xem CONTEXT.md §4.");

                var certificate = LoadCertificate(builder.Configuration);

                builder.WebHost.ConfigureKestrel(kestrel =>
                    kestrel.Listen(address, TailnetPort, listen =>
                    {
                        // §4: HTTPS bắt buộc — không phải vì sợ nghe lén (tailnet
                        // đã mã hoá) mà vì trình duyệt khoá SubtleCrypto, service
                        // worker, clipboard API khi không có HTTPS. Cookie phiên
                        // cũng đặt Secure nên qua HTTP thường sẽ không giữ được.
                        if (certificate is not null)
                        {
                            listen.UseHttps(https =>
                            {
                                https.ServerCertificate = certificate.Leaf;

                                // Gửi cả chuỗi trung gian: thiếu nó thì client
                                // chưa tin root mới của Let's Encrypt sẽ báo
                                // "unable to get local issuer certificate".
                                https.ServerCertificateChain = certificate.Chain;
                            });
                        }
                        else
                        {
                            // Chứng chỉ dev của `dotnet dev-certs`. Chỉ hợp lệ cho
                            // tên "localhost", nên máy khác vào sẽ thấy cảnh báo —
                            // dùng tạm cho tới khi có `tailscale cert`.
                            listen.UseHttps();
                        }
                    }));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Chế độ bind lạ.");
        }
    }

    /// <summary>
    /// Chứng chỉ do `tailscale cert` cấp, nếu có.
    ///
    /// §4: dùng chứng chỉ Let's Encrypt hợp lệ cho tên `.ts.net` — trình duyệt
    /// tin ngay, không cảnh báo trên iPhone. KHÔNG dùng chứng chỉ tự ký: iOS bắt
    /// cài profile thủ công và rất phiền.
    ///
    /// Chưa cấu hình thì trả null và người gọi rơi về chứng chỉ dev.
    /// </summary>
    private static TlsCertificate? LoadCertificate(IConfiguration configuration)
    {
        var certPath = configuration[CertificateKey];
        var keyPath = configuration[CertificateKeyKey];

        if (string.IsNullOrWhiteSpace(certPath) || string.IsNullOrWhiteSpace(keyPath))
        {
            return null;
        }

        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            throw new InvalidOperationException(
                $"Không tìm thấy chứng chỉ tại '{certPath}' hoặc khoá tại '{keyPath}'. " +
                "Chạy `tailscale cert <tên-máy>.ts.net` hoặc bỏ HUB_TLS_CERT/HUB_TLS_KEY.");
        }

        // Nạp CẢ CHUỖI, không chỉ chứng chỉ lá.
        //
        // `tailscale cert` ghi ra 4 chứng chỉ: lá, trung gian YE1, Root YE, và
        // ISRG Root X2. Cái cuối KHÔNG thừa: nó do ISRG Root X1 ký (root cũ, máy
        // nào cũng tin), nên là cầu nối cho client chưa biết Root YE — root
        // ECDSA mới của Let's Encrypt.
        //
        // Đã kiểm chứng bằng `openssl verify`: chuỗi 3 chứng chỉ thì thất bại
        // với "unable to get local issuer certificate", đủ 4 thì OK.
        var chain = new X509Certificate2Collection();
        chain.ImportFromPemFile(certPath);

        if (chain.Count == 0)
        {
            throw new InvalidOperationException($"Không đọc được chứng chỉ nào từ '{certPath}'.");
        }

        using var leaf = X509Certificate2.CreateFromPemFile(certPath, keyPath);

        // Gộp trọn chuỗi vào MỘT file PFX thay vì dùng ServerCertificateChain:
        // Kestrel tự lược các chứng chỉ nó coi là root khỏi chuỗi gửi đi, và
        // ISRG Root X2 bị loại oan. Đưa qua PKCS#12 thì nó gửi nguyên vẹn.
        var bundle = new X509Certificate2Collection { leaf };
        for (var index = 1; index < chain.Count; index++)
        {
            bundle.Add(chain[index]);
        }

        var loaded = X509CertificateLoader.LoadPkcs12Collection(
            bundle.Export(X509ContentType.Pfx)!, null);

        // Chứng chỉ mang khoá riêng là chứng chỉ lá; phần còn lại là chuỗi.
        var leafWithKey = loaded.FirstOrDefault(certificate => certificate.HasPrivateKey)
            ?? throw new InvalidOperationException(
                "Không tìm thấy chứng chỉ kèm khoá riêng sau khi nạp PKCS#12.");

        var intermediates = new X509Certificate2Collection();
        foreach (var certificate in loaded)
        {
            if (!ReferenceEquals(certificate, leafWithKey))
            {
                intermediates.Add(certificate);
            }
        }

        return new TlsCertificate(leafWithKey, intermediates);
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

    /// <summary>
    /// Địa chỉ IPv4 riêng tư trên card mạng VẬT LÝ.
    ///
    /// Loại card ảo (VirtualBox, VMware, WSL, Docker) và VPN (Tailscale, Radmin):
    /// bind nhầm vào chúng thì router không chuyển tiếp tới được, và tệ hơn là
    /// có thể phơi hub ra một mạng khác ngoài dự tính.
    /// </summary>
    private static IPAddress? FindLanAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(nic => nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                or NetworkInterfaceType.Wireless80211)
            .Where(nic => !IsVirtualInterface(nic))
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(addr => addr.Address)
            .Where(addr => addr.AddressFamily == AddressFamily.InterNetwork)
            .Where(IsPrivateLanAddress)
            .FirstOrDefault();
    }

    private static bool IsVirtualInterface(NetworkInterface nic)
    {
        string[] hints =
        [
            "virtualbox", "vmware", "hyper-v", "vethernet", "wsl",
            "docker", "radmin", "tailscale", "zerotier", "loopback"
        ];

        return hints.Any(hint =>
            nic.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)
            || nic.Description.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        var octets = address.GetAddressBytes();

        // Bỏ dải CGNAT của Tailscale — nó không phải LAN vật lý.
        if (octets[0] == 100 && octets[1] >= 64 && octets[1] <= 127)
        {
            return false;
        }

        return octets[0] == 10
            || (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
            || (octets[0] == 192 && octets[1] == 168);
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

namespace Hub.Agent;

/// <summary>Cấu hình agent. Đọc từ appsettings hoặc biến môi trường (§3.3).</summary>
public sealed class AgentSettings
{
    public const string SectionName = "Agent";

    /// <summary>Địa chỉ backend, ví dụ https://100.100.100.100:7189.</summary>
    public string? HubUrl { get; set; }

    /// <summary>
    /// Khoá chung với backend. §6.5 mục 1: không để trong source — dùng User
    /// Secrets lúc dev, biến môi trường lúc chạy thật.
    /// </summary>
    public string? SharedSecret { get; set; }

    /// <summary>Cổng agent lắng nghe. Phải khớp Agent:Port bên backend.</summary>
    public int Port { get; set; } = 5199;

    /// <summary>Khoảng thời gian giữa hai lần báo danh.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Máy này có phải máy đang chạy backend không (§5a điều 5).</summary>
    public bool IsBackendHost { get; set; }
}

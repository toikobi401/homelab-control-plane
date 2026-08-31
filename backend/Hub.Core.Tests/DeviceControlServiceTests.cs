using Hub.Core.Abstractions;
using Hub.Core.Devices;
using Hub.Core.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hub.Core.Tests;

/// <summary>
/// Test cho các quy tắc an toàn của §5a. Đây là năng lực nguy hiểm nhất hệ
/// thống — mỗi test dưới đây tương ứng một điều khoản cụ thể của tài liệu.
/// </summary>
public sealed class DeviceControlServiceTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryDeviceStore _store;
    private readonly RecordingSender _sender = new();

    public DeviceControlServiceTests()
    {
        _store = new InMemoryDeviceStore();
    }

    private DeviceControlService CreateService() => new(
        _store, _sender, _clock, NullLogger<DeviceControlService>.Instance);

    private RegisteredDevice AddDevice(
        bool approved = true,
        bool isBackendHost = false,
        string hostname = "laptop")
    {
        var device = new RegisteredDevice
        {
            Id = Guid.NewGuid(),
            Hostname = hostname,
            OperatingSystem = "windows",
            TailnetAddress = "100.127.197.26",
            IsApproved = approved,
            IsBackendHost = isBackendHost,
            RegisteredAt = _clock.UtcNow,
            LastSeenAt = _clock.UtcNow
        };

        _store.Devices.Add(device);
        return device;
    }

    [Fact]
    public async Task Execute_ThietBiKhongTonTai_BiTuChoi()
    {
        var result = await CreateService().ExecuteAsync(
            Guid.NewGuid(), PowerAction.Shutdown, null);

        Assert.True(result.IsFailure);
        Assert.Empty(_sender.Sent);
    }

    /// <summary>§5a: thiết bị mới phải được duyệt thủ công trước khi nhận lệnh.</summary>
    [Fact]
    public async Task Execute_ChuaDuyet_BiTuChoi()
    {
        var device = AddDevice(approved: false);

        var result = await CreateService().ExecuteAsync(
            device.Id, PowerAction.Shutdown, null);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);

        // Quan trọng: lệnh KHÔNG được chạm tới agent.
        Assert.Empty(_sender.Sent);
    }

    /// <summary>
    /// §5a điều 5: không tắt được máy đang chạy backend. Tự tắt server đang phục
    /// vụ chính request này vừa khó hiểu vừa cắt luôn đường vào hệ thống.
    /// </summary>
    [Theory]
    [InlineData(PowerAction.Shutdown)]
    [InlineData(PowerAction.Restart)]
    [InlineData(PowerAction.Sleep)]
    public async Task Execute_MayChayBackend_KhongTatDuoc(PowerAction action)
    {
        var device = AddDevice(isBackendHost: true, hostname: "War_Machine_2");

        var result = await CreateService().ExecuteAsync(device.Id, action, null);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error!.Value.Code);
        Assert.Contains("War_Machine_2", result.Error!.Value.Message);
        Assert.Empty(_sender.Sent);
    }

    /// <summary>Khoá màn hình không làm backend ngừng phục vụ, nên vẫn cho phép.</summary>
    [Fact]
    public async Task Execute_MayChayBackend_VanKhoaManHinhDuoc()
    {
        var device = AddDevice(isBackendHost: true);

        var result = await CreateService().ExecuteAsync(device.Id, PowerAction.Lock, null);

        Assert.True(result.IsSuccess);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Execute_ThietBiHopLe_GuiLenhToiAgent()
    {
        var device = AddDevice();

        var result = await CreateService().ExecuteAsync(
            device.Id, PowerAction.Restart, null);

        Assert.True(result.IsSuccess);
        var (sentDevice, sentAction) = Assert.Single(_sender.Sent);
        Assert.Equal(device.Id, sentDevice.Id);
        Assert.Equal(PowerAction.Restart, sentAction);
    }

    /// <summary>§5a điều 7: ghi nhật ký kiểm toán MỌI lệnh.</summary>
    [Fact]
    public async Task Execute_ThanhCong_GhiNhatKy()
    {
        var device = AddDevice();
        var sessionId = Guid.NewGuid();

        await CreateService().ExecuteAsync(device.Id, PowerAction.Shutdown, sessionId);

        var audit = Assert.Single(_store.Commands);
        Assert.True(audit.Succeeded);
        Assert.Equal(sessionId, audit.SessionId);
        Assert.Equal(PowerAction.Shutdown, audit.Action);
        Assert.Equal(device.Hostname, audit.DeviceHostname);
    }

    /// <summary>
    /// Lệnh bị chặn cũng phải vào nhật ký: một chuỗi lệnh bị từ chối là dấu hiệu
    /// đáng chú ý, không phải chuyện vô hại để bỏ qua.
    /// </summary>
    [Fact]
    public async Task Execute_BiChan_VanGhiNhatKy()
    {
        var device = AddDevice(approved: false);

        await CreateService().ExecuteAsync(device.Id, PowerAction.Shutdown, null);

        var audit = Assert.Single(_store.Commands);
        Assert.False(audit.Succeeded);
        Assert.Equal("validation", audit.FailureReason);
    }

    [Fact]
    public async Task Execute_AgentLoi_GhiNhatKyThatBai()
    {
        var device = AddDevice();
        _sender.NextResult = Result.Failure(new ResultError("agent_timeout", "Không phản hồi."));

        var result = await CreateService().ExecuteAsync(device.Id, PowerAction.Sleep, null);

        Assert.True(result.IsFailure);
        var audit = Assert.Single(_store.Commands);
        Assert.False(audit.Succeeded);
        Assert.Equal("agent_timeout", audit.FailureReason);
    }

    private sealed class RecordingSender : IAgentCommandSender
    {
        public List<(RegisteredDevice Device, PowerAction Action)> Sent { get; } = [];

        public Result NextResult { get; set; } = Result.Success();

        public Task<Result> SendAsync(
            RegisteredDevice device,
            PowerAction action,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((device, action));
            return Task.FromResult(NextResult);
        }
    }

    private sealed class InMemoryDeviceStore : IDeviceStore
    {
        public List<RegisteredDevice> Devices { get; } = [];

        public List<DeviceCommandAudit> Commands { get; } = [];

        public Task<RegisteredDevice?> GetAsync(Guid deviceId, CancellationToken ct = default)
            => Task.FromResult(Devices.FirstOrDefault(device => device.Id == deviceId));

        public Task<IReadOnlyList<RegisteredDevice>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RegisteredDevice>>(Devices);

        public Task<RegisteredDevice?> FindByHostnameAsync(string hostname, CancellationToken ct = default)
            => Task.FromResult(Devices.FirstOrDefault(device => device.Hostname == hostname));

        public Task AddAsync(RegisteredDevice device, CancellationToken ct = default)
        {
            Devices.Add(device);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(RegisteredDevice device, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid deviceId, CancellationToken ct = default)
            => Task.FromResult(Devices.RemoveAll(device => device.Id == deviceId) > 0);

        public Task RecordCommandAsync(DeviceCommandAudit audit, CancellationToken ct = default)
        {
            Commands.Add(audit);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DeviceCommandAudit>> GetRecentCommandsAsync(
            int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DeviceCommandAudit>>(Commands);
    }
}

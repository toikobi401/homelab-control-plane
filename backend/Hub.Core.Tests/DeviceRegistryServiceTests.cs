using Hub.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hub.Core.Tests;

public sealed class DeviceRegistryServiceTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly StubDeviceStore _store = new();

    private DeviceRegistryService CreateService() => new(
        _store, _clock, NullLogger<DeviceRegistryService>.Instance);

    private static DeviceRegistration Registration(
        string hostname = "laptop",
        string? mac = "AA-BB-CC-DD-EE-FF") => new()
    {
        Hostname = hostname,
        OperatingSystem = "windows",
        TailnetAddress = "100.127.197.26",
        MacAddress = mac,
        LanLabel = "192.168.0.0/24"
    };

    /// <summary>§5a: agent đăng ký xong là ở trạng thái chờ duyệt, không nhận lệnh ngay.</summary>
    [Fact]
    public async Task Register_ThietBiMoi_ChuaDuocDuyet()
    {
        var result = await CreateService().RegisterAsync(Registration());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsApproved);
    }

    /// <summary>
    /// Đăng ký lại KHÔNG được tự nâng quyền. Nếu không, một agent bị chiếm chỉ
    /// cần gọi lại /register là thoát khỏi trạng thái chờ duyệt.
    /// </summary>
    [Fact]
    public async Task Register_LaiSauKhiThuHoi_KhongTuNangQuyen()
    {
        var service = CreateService();
        var first = await service.RegisterAsync(Registration());

        await service.ApproveAsync(first.Value.Id);
        await service.RevokeApprovalAsync(first.Value.Id);

        await service.RegisterAsync(Registration());

        Assert.False(_store.Devices.Single().IsApproved);
    }

    [Fact]
    public async Task Register_LaiVoiCungHostname_KhongTaoBanGhiTrung()
    {
        var service = CreateService();

        await service.RegisterAsync(Registration());
        await service.RegisterAsync(Registration());

        Assert.Single(_store.Devices);
    }

    [Fact]
    public async Task Register_ThieuHostname_BiTuChoi()
    {
        var result = await CreateService().RegisterAsync(Registration(hostname: "  "));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }

    [Fact]
    public async Task Register_MacSaiDinhDang_BiTuChoi()
    {
        var result = await CreateService().RegisterAsync(Registration(mac: "khong-phai-mac"));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }

    /// <summary>
    /// Agent chạy trên Wi-Fi có thể không đọc được MAC của card Ethernet — đừng
    /// xoá mất giá trị đã ghi được lúc trước, vì máy tắt rồi thì không hỏi lại được.
    /// </summary>
    [Fact]
    public async Task Register_LaiMaKhongCoMac_GiuLaiMacCu()
    {
        var service = CreateService();
        await service.RegisterAsync(Registration(mac: "AA-BB-CC-DD-EE-FF"));

        await service.RegisterAsync(Registration(mac: null));

        Assert.Equal("AA:BB:CC:DD:EE:FF", _store.Devices.Single().MacAddress);
    }

    [Theory]
    [InlineData("AA-BB-CC-DD-EE-FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aa:bb:cc:dd:ee:ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AABBCCDDEEFF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabb.ccdd.eeff", "AA:BB:CC:DD:EE:FF")]
    public void NormalizeMac_ChapNhanNhieuDinhDang(string input, string expected)
        => Assert.Equal(expected, DeviceRegistryService.NormalizeMac(input));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("AA:BB:CC")]
    [InlineData("AA:BB:CC:DD:EE:FF:00")]
    [InlineData("ZZ:BB:CC:DD:EE:FF")]
    public void NormalizeMac_TraNullKhiKhongHopLe(string? input)
        => Assert.Null(DeviceRegistryService.NormalizeMac(input));

    [Fact]
    public async Task Delete_GoThietBiKhoiSo()
    {
        var service = CreateService();
        var device = await service.RegisterAsync(Registration());

        var result = await service.DeleteAsync(device.Value.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_store.Devices);
    }

    [Fact]
    public async Task Delete_ThietBiKhongTonTai_BiTuChoi()
    {
        var result = await CreateService().DeleteAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }

    /// <summary>
    /// Gỡ rồi đăng ký lại thì phải chờ duyệt từ đầu — không tự lấy lại quyền cũ.
    /// </summary>
    [Fact]
    public async Task Delete_RoiDangKyLai_LaiChoDuyet()
    {
        var service = CreateService();
        var device = await service.RegisterAsync(Registration());
        await service.ApproveAsync(device.Value.Id);
        await service.DeleteAsync(device.Value.Id);

        var again = await service.RegisterAsync(Registration());

        Assert.False(again.Value.IsApproved);
    }

    [Fact]
    public async Task Approve_ThietBiKhongTonTai_BiTuChoi()
    {
        var result = await CreateService().ApproveAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
    }

    private sealed class StubDeviceStore : IDeviceStore
    {
        public List<RegisteredDevice> Devices { get; } = [];

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
            => Task.CompletedTask;

        public Task<IReadOnlyList<DeviceCommandAudit>> GetRecentCommandsAsync(
            int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DeviceCommandAudit>>([]);
    }
}

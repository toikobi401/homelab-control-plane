using Hub.Core.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hub.Core.Tests;

/// <summary>
/// Test cho các yêu cầu của §6.3. Mỗi test nhắm vào một quy tắc cụ thể của
/// tài liệu, không phải test lấy lệ.
/// </summary>
public sealed class AuthServiceTests
{
    private const string ValidPassword = "mat-khau-du-dai-123";

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly FakePasswordHasher _hasher = new();
    private readonly InMemoryAuthStore _store;
    private readonly AuthOptions _options = new();

    public AuthServiceTests()
    {
        _store = new InMemoryAuthStore(_clock);
    }

    private AuthService CreateService() => new(
        _store,
        _hasher,
        _clock,
        Options.Create(_options),
        NullLogger<AuthService>.Instance);

    private async Task<AuthService> CreateServiceWithPasswordAsync()
    {
        var service = CreateService();
        var result = await service.SetInitialPasswordAsync(ValidPassword);
        Assert.True(result.IsSuccess);
        return service;
    }

    [Fact]
    public async Task GetStatus_ChuaDatMatKhau_TraVeFalse()
    {
        var status = await CreateService().GetStatusAsync();
        Assert.False(status.PasswordConfigured);
    }

    [Fact]
    public async Task SetInitialPassword_LanHai_BiTuChoi()
    {
        var service = await CreateServiceWithPasswordAsync();

        // §6.3: đặt đè không được phép — phải đi qua đổi mật khẩu (bắt nhập cũ).
        var result = await service.SetInitialPasswordAsync("mat-khau-khac-du-dai");

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error!.Value.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ngan")]
    [InlineData("11-ky-tu-a")]
    public async Task SetInitialPassword_QuaNgan_BiTuChoi(string password)
    {
        var result = await CreateService().SetInitialPasswordAsync(password);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }

    [Fact]
    public async Task Login_DungMatKhau_TaoPhienMoi()
    {
        var service = await CreateServiceWithPasswordAsync();

        var result = await service.LoginAsync(ValidPassword, "iPhone", "100.64.0.5");

        Assert.True(result.IsSuccess);
        Assert.Equal("iPhone", result.Value.Session.Device);
        Assert.Equal("100.64.0.5", result.Value.Session.TailnetAddress);
    }

    [Fact]
    public async Task Login_SaiMatKhau_KhongTaoPhien()
    {
        var service = await CreateServiceWithPasswordAsync();

        var result = await service.LoginAsync("sai-mat-khau-roi", "iPhone", null);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error!.Value.Code);
        Assert.Empty(await service.GetActiveSessionsAsync());
    }

    /// <summary>
    /// §6.3: thời gian phản hồi phải như nhau dù sai tài khoản hay sai mật khẩu.
    /// Chưa đặt mật khẩu mà return sớm là lộ thông tin qua thời gian, nên bản
    /// hiện thực vẫn phải băm một lần.
    /// </summary>
    [Fact]
    public async Task Login_ChuaDatMatKhau_VanBamMotLan()
    {
        var service = CreateService();
        var before = _hasher.HashCallCount;

        var result = await service.LoginAsync(ValidPassword, "iPhone", null);

        Assert.True(result.IsFailure);
        Assert.Equal(before + 1, _hasher.HashCallCount);
    }

    /// <summary>§6.3: chống session fixation — mỗi lần đăng nhập là một id mới.</summary>
    [Fact]
    public async Task Login_HaiLan_SinhHaiIdKhacNhau()
    {
        var service = await CreateServiceWithPasswordAsync();

        var first = await service.LoginAsync(ValidPassword, "iPhone", null);
        var second = await service.LoginAsync(ValidPassword, "Android", null);

        Assert.NotEqual(first.Value.Session.Id, second.Value.Session.Id);
    }

    [Fact]
    public async Task Login_SaiQuaNhieuLan_BiKhoa()
    {
        var service = await CreateServiceWithPasswordAsync();

        for (var attempt = 0; attempt < _options.FailedAttemptsBeforeLockout; attempt++)
        {
            await service.LoginAsync("sai-mat-khau-roi", "iPhone", null);
        }

        // Lần này đúng mật khẩu, nhưng đang bị khoá nên vẫn phải từ chối.
        var result = await service.LoginAsync(ValidPassword, "iPhone", null);

        Assert.True(result.IsFailure);
        Assert.Equal("too_many_attempts", result.Error!.Value.Code);
    }

    [Fact]
    public async Task Login_HetCuaSoKhoa_ChoPhepLais()
    {
        var service = await CreateServiceWithPasswordAsync();

        for (var attempt = 0; attempt < _options.FailedAttemptsBeforeLockout; attempt++)
        {
            await service.LoginAsync("sai-mat-khau-roi", "iPhone", null);
        }

        // Qua khỏi cửa sổ tính số lần sai thì lịch sử cũ không còn tính nữa.
        _clock.Advance(_options.LockoutWindow + TimeSpan.FromMinutes(1));

        var result = await service.LoginAsync(ValidPassword, "iPhone", null);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Khoá tăng dần không được tràn số. Trước đây dùng Math.Pow thì overage lớn
    /// cho ra Infinity; bản hiện tại dịch bit và chặn trần.
    /// </summary>
    [Fact]
    public async Task Login_RatNhieuLanSai_ThoiGianKhoaKhongTran()
    {
        var service = await CreateServiceWithPasswordAsync();

        for (var attempt = 0; attempt < 100; attempt++)
        {
            await service.LoginAsync("sai-mat-khau-roi", "iPhone", null);
        }

        var result = await service.LoginAsync(ValidPassword, "iPhone", null);

        Assert.True(result.IsFailure);
        Assert.Equal("too_many_attempts", result.Error!.Value.Code);

        // Thông báo phải chứa một con số hữu hạn, không phải Infinity/NaN.
        var message = result.Error!.Value.Message;
        Assert.DoesNotContain("Infinity", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NaN", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_ThanhCong_XoaLichSuSai()
    {
        var service = await CreateServiceWithPasswordAsync();

        // Sai vài lần nhưng chưa tới ngưỡng khoá.
        for (var attempt = 0; attempt < _options.FailedAttemptsBeforeLockout - 1; attempt++)
        {
            await service.LoginAsync("sai-mat-khau-roi", "iPhone", null);
        }

        Assert.True((await service.LoginAsync(ValidPassword, "iPhone", null)).IsSuccess);

        // Sau khi thành công, đếm lại từ đầu: sai thêm vài lần vẫn chưa bị khoá.
        for (var attempt = 0; attempt < _options.FailedAttemptsBeforeLockout - 1; attempt++)
        {
            await service.LoginAsync("sai-mat-khau-roi", "iPhone", null);
        }

        Assert.True((await service.LoginAsync(ValidPassword, "iPhone", null)).IsSuccess);
    }

    [Fact]
    public async Task ValidateSession_PhienHetHan_BiTuChoi()
    {
        var service = await CreateServiceWithPasswordAsync();
        var login = await service.LoginAsync(ValidPassword, "iPhone", null);

        _clock.Advance(_options.SessionLifetime + TimeSpan.FromMinutes(1));

        var result = await service.ValidateSessionAsync(login.Value.Session.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error!.Value.Code);
    }

    [Fact]
    public async Task ValidateSession_SapHetHan_DuocGiaHan()
    {
        var service = await CreateServiceWithPasswordAsync();
        var login = await service.LoginAsync(ValidPassword, "iPhone", null);
        var originalExpiry = login.Value.Session.ExpiresAt;

        // Nhảy tới lúc phiên còn ít hơn ngưỡng gia hạn.
        _clock.Advance(_options.SessionLifetime - _options.SlidingRenewalThreshold
            + TimeSpan.FromMinutes(1));

        var result = await service.ValidateSessionAsync(login.Value.Session.Id);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ExpiresAt > originalExpiry);
    }

    [Fact]
    public async Task ValidateSession_PhienDaThuHoi_BiTuChoi()
    {
        var service = await CreateServiceWithPasswordAsync();
        var login = await service.LoginAsync(ValidPassword, "iPhone", null);

        await service.RevokeSessionAsync(login.Value.Session.Id);

        var result = await service.ValidateSessionAsync(login.Value.Session.Id);
        Assert.True(result.IsFailure);
    }

    /// <summary>§6.3: "đăng xuất tất cả thiết bị" — phương án ứng phó khi mất máy.</summary>
    [Fact]
    public async Task RevokeAll_ThuHoiMoiPhien()
    {
        var service = await CreateServiceWithPasswordAsync();
        await service.LoginAsync(ValidPassword, "iPhone", null);
        await service.LoginAsync(ValidPassword, "Android", null);
        await service.LoginAsync(ValidPassword, "PC", null);

        var revoked = await service.RevokeAllSessionsAsync();

        Assert.Equal(3, revoked);
        Assert.Empty(await service.GetActiveSessionsAsync());
    }

    [Fact]
    public async Task RevokeAll_GiuLaiPhienHienTai()
    {
        var service = await CreateServiceWithPasswordAsync();
        var keep = await service.LoginAsync(ValidPassword, "PC", null);
        await service.LoginAsync(ValidPassword, "iPhone", null);

        var revoked = await service.RevokeAllSessionsAsync(keep.Value.Session.Id);

        Assert.Equal(1, revoked);
        var remaining = await service.GetActiveSessionsAsync();
        Assert.Equal(keep.Value.Session.Id, Assert.Single(remaining).Id);
    }

    [Fact]
    public async Task ChangePassword_SaiMatKhauCu_BiTuChoi()
    {
        var service = await CreateServiceWithPasswordAsync();

        var result = await service.ChangePasswordAsync(
            "sai-mat-khau-cu-roi", "mat-khau-moi-du-dai", null);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error!.Value.Code);
    }

    /// <summary>§6.3: đổi mật khẩu phải huỷ toàn bộ phiên khác.</summary>
    [Fact]
    public async Task ChangePassword_ThanhCong_HuyCacPhienKhac()
    {
        var service = await CreateServiceWithPasswordAsync();
        var current = await service.LoginAsync(ValidPassword, "PC", null);
        await service.LoginAsync(ValidPassword, "iPhone", null);
        await service.LoginAsync(ValidPassword, "Android", null);

        const string newPassword = "mat-khau-moi-du-dai";
        var result = await service.ChangePasswordAsync(
            ValidPassword, newPassword, current.Value.Session.Id);

        Assert.True(result.IsSuccess);

        // Chỉ còn phiên hiện tại.
        var remaining = await service.GetActiveSessionsAsync();
        Assert.Equal(current.Value.Session.Id, Assert.Single(remaining).Id);

        // Và mật khẩu mới có hiệu lực thật.
        Assert.True((await service.LoginAsync(newPassword, "PC", null)).IsSuccess);
    }

    [Fact]
    public async Task ChangePassword_MatKhauMoiQuaNgan_BiTuChoi()
    {
        var service = await CreateServiceWithPasswordAsync();

        var result = await service.ChangePasswordAsync(ValidPassword, "ngan", null);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }
}

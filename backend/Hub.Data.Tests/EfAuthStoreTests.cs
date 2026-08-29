using Hub.Core.Abstractions;
using Hub.Core.Authentication;
using Hub.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hub.Data.Tests;

/// <summary>
/// Test chạy trên SQLite THẬT (in-memory), không phải provider InMemory của EF.
///
/// Lý do: provider InMemory không dịch query sang SQL nên nó nuốt hết lỗi dịch
/// thuật. Một bug thật đã lọt qua test đơn vị đúng vì kiểu này — SQLite không
/// so sánh được DateTimeOffset, và chỉ lộ ra khi gọi API thật. Những test dưới
/// đây tồn tại để chuyện đó không tái diễn.
/// </summary>
public sealed class EfAuthStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private HubDbContext _dbContext = null!;
    private EfAuthStore _store = null!;

    public async Task InitializeAsync()
    {
        // Giữ connection mở: SQLite in-memory xoá sạch DB khi connection cuối đóng.
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new HubDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _store = new EfAuthStore(_dbContext, _clock);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Chính là query từng ném InvalidOperationException trên SQLite thật.
    /// </summary>
    [Fact]
    public async Task CountFailedLoginsSince_DichDuocSangSql()
    {
        await _store.RecordFailedLoginAsync("100.64.0.5");
        await _store.RecordFailedLoginAsync("100.64.0.5");

        var count = await _store.CountFailedLoginsSinceAsync(_clock.UtcNow.AddMinutes(-15));

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountFailedLoginsSince_ChiDemTrongCuaSo()
    {
        await _store.RecordFailedLoginAsync(null);

        _clock.Advance(TimeSpan.FromHours(1));
        await _store.RecordFailedLoginAsync(null);

        // Chỉ tính 30 phút gần nhất — lần sai một giờ trước không được tính.
        var count = await _store.CountFailedLoginsSinceAsync(_clock.UtcNow.AddMinutes(-30));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetActiveSessions_LocPhienHetHan()
    {
        await _store.AddSessionAsync(NewSession("con-han", _clock.UtcNow.AddDays(30)));
        await _store.AddSessionAsync(NewSession("het-han", _clock.UtcNow.AddDays(-1)));

        var active = await _store.GetActiveSessionsAsync();

        Assert.Equal("con-han", Assert.Single(active).Device);
    }

    [Fact]
    public async Task RevokeAllSessions_ChayDuocTrenSqlite()
    {
        var keep = NewSession("giu-lai", _clock.UtcNow.AddDays(30));
        await _store.AddSessionAsync(keep);
        await _store.AddSessionAsync(NewSession("thu-hoi-1", _clock.UtcNow.AddDays(30)));
        await _store.AddSessionAsync(NewSession("thu-hoi-2", _clock.UtcNow.AddDays(30)));

        var revoked = await _store.RevokeAllSessionsAsync(keep.Id);

        Assert.Equal(2, revoked);
        Assert.Equal("giu-lai", Assert.Single(await _store.GetActiveSessionsAsync()).Device);
    }

    [Fact]
    public async Task RevokeSession_LanHai_TraVeFalse()
    {
        var session = NewSession("iPhone", _clock.UtcNow.AddDays(30));
        await _store.AddSessionAsync(session);

        Assert.True(await _store.RevokeSessionAsync(session.Id));
        Assert.False(await _store.RevokeSessionAsync(session.Id));
    }

    /// <summary>
    /// Value converter lưu ticks UTC. Test này bắt lỗi nếu ai đó đổi converter
    /// và làm mất thông tin múi giờ khi đọc lại.
    /// </summary>
    [Fact]
    public async Task ThoiGian_GiuNguyenSauKhiDocLai()
    {
        var expiresAt = new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.FromHours(7));
        var session = NewSession("iPhone", expiresAt);
        await _store.AddSessionAsync(session);

        _dbContext.ChangeTracker.Clear();
        var loaded = await _store.GetSessionAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.Equal(expiresAt.ToUniversalTime(), loaded.ExpiresAt.ToUniversalTime());
    }

    [Fact]
    public async Task SaveCredential_GoiHaiLan_CapNhatChuKhongTaoThem()
    {
        await _store.SaveCredentialAsync(new Credential
        {
            Id = 1,
            PasswordHash = "hash-cu",
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow
        });

        await _store.SaveCredentialAsync(new Credential
        {
            Id = 1,
            PasswordHash = "hash-moi",
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow
        });

        Assert.Equal(1, await _dbContext.Credentials.CountAsync());
        Assert.Equal("hash-moi", (await _store.GetCredentialAsync())!.PasswordHash);
    }

    [Fact]
    public async Task ClearFailedLogins_XoaHet()
    {
        await _store.RecordFailedLoginAsync(null);
        await _store.RecordFailedLoginAsync(null);

        await _store.ClearFailedLoginsAsync();

        Assert.Equal(0, await _store.CountFailedLoginsSinceAsync(_clock.UtcNow.AddDays(-1)));
    }

    private Session NewSession(string device, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        Device = device,
        TailnetAddress = "100.64.0.5",
        CreatedAt = _clock.UtcNow,
        LastSeenAt = _clock.UtcNow,
        ExpiresAt = expiresAt
    };

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}

using Hub.Core.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hub.Data;

/// <summary>
/// DbContext của hệ thống. Một file SQLite duy nhất (§3) — chuyển máy chỉ là
/// chép file, nên hợp với hướng chuyển sang NAS (§3.3).
/// </summary>
public sealed class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
    /// <summary>
    /// SQLite không có kiểu ngày giờ thật, nên EF không dịch được phép so sánh
    /// trên <see cref="DateTimeOffset"/> (query kiểu "đếm số lần sai kể từ X"
    /// sẽ ném InvalidOperationException lúc chạy).
    ///
    /// Lưu thành ticks UTC: so sánh được bằng số nguyên, và giữ nguyên thứ tự
    /// thời gian. Chuẩn hoá về UTC trước khi lấy ticks — nếu không, hai thời
    /// điểm bằng nhau ở hai offset khác nhau sẽ ra hai số khác nhau.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, long> DateTimeOffsetToTicks = new(
        value => value.UtcDateTime.Ticks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableDateTimeOffsetToTicks = new(
        value => value == null ? null : value.Value.UtcDateTime.Ticks,
        ticks => ticks == null ? null : new DateTimeOffset(ticks.Value, TimeSpan.Zero));

    public DbSet<Credential> Credentials => Set<Credential>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<FailedLoginAttempt> FailedLoginAttempts => Set<FailedLoginAttempt>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Credential>(entity =>
        {
            entity.HasKey(credential => credential.Id);

            // Không dùng khoá tự tăng: §6.3 quy định đúng một người dùng, nên
            // hàng này luôn là Id = 1.
            entity.Property(credential => credential.Id).ValueGeneratedNever();
            entity.Property(credential => credential.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Device).IsRequired().HasMaxLength(200);
            entity.Property(session => session.TailnetAddress).HasMaxLength(64);

            // Mọi request đã xác thực đều tra phiên còn hiệu lực — đánh chỉ mục
            // để không quét bảng khi số phiên lớn dần.
            entity.HasIndex(session => new { session.RevokedAt, session.ExpiresAt });
        });

        modelBuilder.Entity<FailedLoginAttempt>(entity =>
        {
            entity.HasKey(attempt => attempt.Id);
            entity.Property(attempt => attempt.TailnetAddress).HasMaxLength(64);

            // Đếm số lần sai trong cửa sổ thời gian ở mỗi lần đăng nhập.
            entity.HasIndex(attempt => attempt.AttemptedAt);
        });


        ApplyDateTimeOffsetConversions(modelBuilder);
    }

    /// <summary>
    /// Áp value converter cho MỌI thuộc tính DateTimeOffset, thay vì khai từng
    /// cái một — thêm entity mới sau này sẽ không lặp lại lỗi không dịch được query.
    /// </summary>
    private static void ApplyDateTimeOffsetConversions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(DateTimeOffsetToTicks);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(NullableDateTimeOffsetToTicks);
                }
            }
        }
    }
}


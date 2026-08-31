using Hub.Core.Abstractions;
using Hub.Core.Authentication;
using Hub.Data;
using Microsoft.EntityFrameworkCore;

namespace Hub.Api.Authentication;

/// <summary>
/// Đặt lại mật khẩu từ dòng lệnh khi quên mật khẩu cũ.
///
/// §6.3 quy định đổi mật khẩu phải nhập mật khẩu cũ — nhưng quy tắc đó bảo vệ
/// endpoint HTTP, nơi kẻ tấn công có thể chạm tới. Công cụ này chạy **trên chính
/// máy có file DB**: ai chạy được nó thì đã đọc/ghi được DB rồi, nên bắt nhập
/// mật khẩu cũ không thêm lớp bảo vệ nào.
///
/// Vẫn giữ hai điều của §6.3:
/// - Băm bằng <c>PasswordHasher</c> của ASP.NET Identity, KHÔNG tự viết crypto.
/// - **Huỷ toàn bộ phiên đang mở** — đặt lại mật khẩu thường vì nghi lộ.
/// </summary>
internal static class PasswordResetTool
{
    public const string Flag = "--reset-password";

    public static async Task<int> RunAsync(string[] args, string databasePath)
    {
        var password = ReadPassword(args);

        if (password is null)
        {
            return 1;
        }

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var dbContext = new HubDbContext(options);
        await dbContext.Database.MigrateAsync();

        var hasher = new IdentityPasswordHasher();
        var clock = new SystemClock();
        var now = clock.UtcNow;

        var credential = await dbContext.Credentials.FirstOrDefaultAsync(row => row.Id == 1);

        if (credential is null)
        {
            dbContext.Credentials.Add(new Credential
            {
                Id = 1,
                PasswordHash = hasher.Hash(password),
                CreatedAt = now,
                UpdatedAt = now
            });

            Console.WriteLine("Đã đặt mật khẩu (trước đó chưa có).");
        }
        else
        {
            credential.PasswordHash = hasher.Hash(password);
            credential.UpdatedAt = now;

            Console.WriteLine("Đã đổi mật khẩu.");
        }

        // §6.3: đổi mật khẩu thì huỷ mọi phiên khác. Ở đây huỷ TẤT CẢ — công cụ
        // này chạy ngoài phiên nào cả, và người quên mật khẩu thường muốn đá
        // mọi thiết bị ra.
        var revoked = await dbContext.Sessions
            .Where(session => session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                session => session.RevokedAt, now));

        await dbContext.SaveChangesAsync();

        Console.WriteLine($"Đã thu hồi {revoked} phiên đang mở — mọi thiết bị phải đăng nhập lại.");
        Console.WriteLine($"DB: {databasePath}");

        return 0;
    }

    /// <summary>
    /// Đọc mật khẩu mới. Ưu tiên hỏi để không lọt vào lịch sử shell; nhận qua
    /// tham số khi chạy tự động.
    /// </summary>
    private static string? ReadPassword(string[] args)
    {
        var index = Array.IndexOf(args, Flag);

        if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--"))
        {
            Console.WriteLine(
                "Cảnh báo: mật khẩu truyền qua dòng lệnh sẽ nằm trong lịch sử shell.");
            return args[index + 1];
        }

        if (!Console.IsInputRedirected)
        {
            Console.Write("Mật khẩu mới: ");
            var typed = ReadHidden();
            Console.WriteLine();

            if (typed.Length < 12)
            {
                Console.Error.WriteLine("Mật khẩu phải dài ít nhất 12 ký tự.");
                return null;
            }

            Console.Write("Nhập lại: ");
            var again = ReadHidden();
            Console.WriteLine();

            if (typed != again)
            {
                Console.Error.WriteLine("Hai lần nhập không khớp.");
                return null;
            }

            return typed;
        }

        Console.Error.WriteLine($"Dùng: dotnet run -- {Flag} \"<mật khẩu>\"");
        return null;
    }

    /// <summary>Đọc không hiện ký tự — mật khẩu không nên nằm trên màn hình.</summary>
    private static string ReadHidden()
    {
        var builder = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
    }
}

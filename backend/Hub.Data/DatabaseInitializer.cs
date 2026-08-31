using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hub.Data;

/// <summary>
/// Đưa schema DB lên bản mới nhất lúc khởi động, bằng EF migration.
///
/// Trước đây dùng <c>EnsureCreated()</c>: nó CHỈ tạo DB mới, không nâng cấp DB
/// đã tồn tại. Hệ quả là khi thêm bảng cho năng lực 6, DB cũ vẫn thiếu bảng và
/// chết lúc chạy với "no such table: Devices".
/// </summary>
public static class DatabaseInitializer
{
    public static async Task MigrateAsync(HubDbContext dbContext, ILogger logger)
    {
        // DB tạo bằng EnsureCreated không có bảng lịch sử migration. Chạy
        // Migrate() thẳng lên đó sẽ cố tạo lại bảng đã có và chết với
        // "table Credentials already exists". Phải nhận nuôi nó trước.
        if (await NeedsAdoptionAsync(dbContext))
        {
            await AdoptExistingDatabaseAsync(dbContext, logger);
        }

        var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count > 0)
        {
            logger.LogInformation(
                "Áp dụng {Count} migration: {Migrations}",
                pending.Count, string.Join(", ", pending));
        }

        await dbContext.Database.MigrateAsync();
    }

    /// <summary>
    /// DB đã có bảng của ứng dụng nhưng chưa có lịch sử migration — dấu hiệu
    /// của DB do <c>EnsureCreated</c> tạo ở các bản trước.
    /// </summary>
    private static async Task<bool> NeedsAdoptionAsync(HubDbContext dbContext)
    {
        if (!await dbContext.Database.CanConnectAsync())
        {
            return false;
        }

        if (await TableExistsAsync(dbContext, "__EFMigrationsHistory"))
        {
            return false;
        }

        // Credentials có trong mọi phiên bản schema trước đây.
        return await TableExistsAsync(dbContext, "Credentials");
    }

    /// <summary>
    /// Nhận nuôi một DB có sẵn: tạo bù những bảng còn thiếu, rồi ghi nhận
    /// migration đầu là đã áp dụng.
    ///
    /// Làm thủ công thay vì xoá DB đi tạo lại vì DB đang chứa **mật khẩu đã băm
    /// và phiên đăng nhập** — xoá là người dùng phải đặt lại mật khẩu và mọi
    /// thiết bị bị đăng xuất.
    /// </summary>
    private static async Task AdoptExistingDatabaseAsync(HubDbContext dbContext, ILogger logger)
    {
        logger.LogWarning(
            "DB được tạo bằng EnsureCreated (không có lịch sử migration). " +
            "Tạo bù bảng còn thiếu rồi chuyển sang quản lý bằng migration.");

        // Lấy đúng các câu lệnh của migration đầu, nhưng chỉ chạy phần tạo bảng
        // chưa tồn tại. SQLite không có CREATE TABLE IF NOT EXISTS trong script
        // EF sinh ra, nên kiểm tra từng bảng.
        foreach (var (table, sql) in GetMissingTableScripts())
        {
            if (await TableExistsAsync(dbContext, table))
            {
                continue;
            }

            logger.LogWarning("Tạo bù bảng thiếu: {Table}", table);
            await dbContext.Database.ExecuteSqlRawAsync(sql);
        }

        await MarkMigrationsAppliedAsync(dbContext);
    }

    /// <summary>
    /// Câu lệnh tạo các bảng được thêm sau khi DB cũ đã tồn tại (năng lực 6).
    ///
    /// Giữ khớp với migration <c>InitialSchema</c>. Đây là mã chỉ chạy một lần
    /// cho DB đời cũ; DB tạo mới đi thẳng qua migration nên không đụng tới đây.
    /// </summary>
    private static IEnumerable<(string Table, string Sql)> GetMissingTableScripts()
    {
        yield return ("Devices",
            """
            CREATE TABLE "Devices" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Devices" PRIMARY KEY,
                "Hostname" TEXT NOT NULL,
                "OperatingSystem" TEXT NOT NULL,
                "TailnetAddress" TEXT NULL,
                "MacAddress" TEXT NULL,
                "LanLabel" TEXT NULL,
                "IsApproved" INTEGER NOT NULL,
                "RegisteredAt" INTEGER NOT NULL,
                "LastSeenAt" INTEGER NOT NULL,
                "IsBackendHost" INTEGER NOT NULL
            );

            CREATE UNIQUE INDEX "IX_Devices_Hostname" ON "Devices" ("Hostname");
            """);

        yield return ("DeviceCommands",
            """
            CREATE TABLE "DeviceCommands" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DeviceCommands" PRIMARY KEY AUTOINCREMENT,
                "RequestedAt" INTEGER NOT NULL,
                "SessionId" TEXT NULL,
                "DeviceId" TEXT NOT NULL,
                "DeviceHostname" TEXT NOT NULL,
                "Action" TEXT NOT NULL,
                "Succeeded" INTEGER NOT NULL,
                "FailureReason" TEXT NULL
            );

            CREATE INDEX "IX_DeviceCommands_RequestedAt" ON "DeviceCommands" ("RequestedAt");
            """);
    }

    private static async Task MarkMigrationsAppliedAsync(HubDbContext dbContext)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """);

        // Đánh dấu MỌI migration đã có là đã áp dụng — schema vừa được đưa về
        // đúng trạng thái của chúng ở trên.
        foreach (var migrationId in dbContext.Database.GetMigrations())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ({0}, {1})
                """,
                migrationId,
                "10.0.11");
        }
    }

    private static async Task<bool> TableExistsAsync(HubDbContext dbContext, string tableName)
    {
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }
}

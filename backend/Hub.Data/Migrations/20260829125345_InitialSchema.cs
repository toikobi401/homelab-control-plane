using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Credentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceHostname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OperatingSystem = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TailnetAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MacAddress = table.Column<string>(type: "TEXT", maxLength: 17, nullable: true),
                    LanLabel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    RegisteredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    IsBackendHost = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FailedLoginAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttemptedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    TailnetAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedLoginAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Device = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TailnetAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCommands_RequestedAt",
                table: "DeviceCommands",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Hostname",
                table: "Devices",
                column: "Hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailedLoginAttempts_AttemptedAt",
                table: "FailedLoginAttempts",
                column: "AttemptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_RevokedAt_ExpiresAt",
                table: "Sessions",
                columns: new[] { "RevokedAt", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Credentials");

            migrationBuilder.DropTable(
                name: "DeviceCommands");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "FailedLoginAttempts");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}

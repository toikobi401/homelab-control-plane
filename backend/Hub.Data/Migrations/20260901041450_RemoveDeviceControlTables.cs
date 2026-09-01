using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDeviceControlTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceCommands");

            migrationBuilder.DropTable(
                name: "Devices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DeviceHostname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RequestedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false)
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
                    AgentLastSeenAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBackendHost = table.Column<bool>(type: "INTEGER", nullable: false),
                    LanLabel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    MacAddress = table.Column<string>(type: "TEXT", maxLength: 17, nullable: true),
                    OperatingSystem = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RegisteredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    TailnetAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
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
        }
    }
}

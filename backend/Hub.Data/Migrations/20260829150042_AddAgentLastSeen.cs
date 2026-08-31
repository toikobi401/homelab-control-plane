using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentLastSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AgentLastSeenAt",
                table: "Devices",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentLastSeenAt",
                table: "Devices");
        }
    }
}

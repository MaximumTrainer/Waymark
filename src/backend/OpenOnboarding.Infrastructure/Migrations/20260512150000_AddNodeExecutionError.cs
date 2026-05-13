using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenOnboarding.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20260512150000_AddNodeExecutionError")]
    public partial class AddNodeExecutionError : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutionErrorJson",
                table: "Nodes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutionErrorJson",
                table: "Nodes");
        }
    }
}

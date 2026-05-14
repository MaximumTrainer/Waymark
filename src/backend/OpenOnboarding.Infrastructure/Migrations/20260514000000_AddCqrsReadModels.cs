using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenOnboarding.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCqrsReadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionReadModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CustomerCountry = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ExternalCustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrentNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentNodeKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CurrentNodeTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StatusName = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StepCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AbandonedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletionDurationSeconds = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionReadModels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionReadModels_FlowId",
                table: "SessionReadModels",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReadModels_FlowId_StatusName",
                table: "SessionReadModels",
                columns: new[] { "FlowId", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionReadModels_StatusName",
                table: "SessionReadModels",
                column: "StatusName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionReadModels");
        }
    }
}

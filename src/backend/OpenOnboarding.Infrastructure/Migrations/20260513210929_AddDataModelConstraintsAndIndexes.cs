using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenOnboarding.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataModelConstraintsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_WebhookId",
                table: "WebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_FlowId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Nodes_FlowId",
                table: "Nodes");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Webhooks",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                table: "Webhooks",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Webhooks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Nodes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "Nodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Nodes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Nodes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Flows",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Flows",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Flows",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Flows",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<string>(
                name: "ExternalCustomerId",
                table: "CustomerProfiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "CustomerProfiles",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Country",
                table: "CustomerProfiles",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "CustomerProfiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "CustomerProfiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookId_Status",
                table: "WebhookDeliveries",
                columns: new[] { "WebhookId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_FlowId_Status",
                table: "Sessions",
                columns: new[] { "FlowId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Status",
                table: "Sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_FlowId_IsStartNode",
                table: "Nodes",
                columns: new[] { "FlowId", "IsStartNode" });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_FlowId_Key",
                table: "Nodes",
                columns: new[] { "FlowId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProfiles_ExternalCustomerId",
                table: "CustomerProfiles",
                column: "ExternalCustomerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_WebhookId_Status",
                table: "WebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_FlowId_Status",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_Status",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Nodes_FlowId_IsStartNode",
                table: "Nodes");

            migrationBuilder.DropIndex(
                name: "IX_Nodes_FlowId_Key",
                table: "Nodes");

            migrationBuilder.DropIndex(
                name: "IX_CustomerProfiles_ExternalCustomerId",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Flows");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Flows");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "CustomerProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Webhooks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                table: "Webhooks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Nodes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "Nodes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Flows",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Flows",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExternalCustomerId",
                table: "CustomerProfiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "CustomerProfiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AlterColumn<string>(
                name: "Country",
                table: "CustomerProfiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookId",
                table: "WebhookDeliveries",
                column: "WebhookId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_FlowId",
                table: "Sessions",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_FlowId",
                table: "Nodes",
                column: "FlowId");
        }
    }
}

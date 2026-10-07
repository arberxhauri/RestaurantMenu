using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddPaddle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_SubscriptionId_PeriodStartUtc",
                table: "Invoices");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProviderSyncedUtc",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaddleArchivedUtc",
                table: "PriceBook",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CardPaymentsEnabled",
                table: "PlanSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PaddleProducts",
                columns: table => new
                {
                    Module = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProductId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaddleProducts", x => x.Module);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ProviderRef",
                table: "Invoices",
                column: "ProviderRef");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SubscriptionId_PeriodStartUtc",
                table: "Invoices",
                columns: new[] { "SubscriptionId", "PeriodStartUtc" },
                unique: true,
                filter: "\"Status\" <> 'Void' AND \"Provider\" = 'BankTransfer'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaddleProducts");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ProviderRef",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_SubscriptionId_PeriodStartUtc",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ProviderSyncedUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PaddleArchivedUtc",
                table: "PriceBook");

            migrationBuilder.DropColumn(
                name: "CardPaymentsEnabled",
                table: "PlanSettings");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SubscriptionId_PeriodStartUtc",
                table: "Invoices",
                columns: new[] { "SubscriptionId", "PeriodStartUtc" },
                unique: true,
                filter: "\"Status\" <> 'Void'");
        }
    }
}

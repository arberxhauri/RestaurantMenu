using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextBranchQuantity",
                table: "Subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextInterval",
                table: "Subscriptions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextModules",
                table: "Subscriptions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceDueDays",
                table: "PlanSettings",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<string>(
                name: "OperatorAddress",
                table: "PlanSettings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorBank",
                table: "PlanSettings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorEmail",
                table: "PlanSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorIban",
                table: "PlanSettings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorName",
                table: "PlanSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorNipt",
                table: "PlanSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorSwift",
                table: "PlanSettings",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalLeadDays",
                table: "PlanSettings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<decimal>(
                name: "VatPercent",
                table: "PlanSettings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "BillingEmailLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEmailLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Interval = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BranchQuantity = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SubtotalCents = table.Column<int>(type: "integer", nullable: false),
                    VatPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    VatCents = table.Column<int>(type: "integer", nullable: false),
                    TotalCents = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IssuedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VoidedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FiscalCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SellerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SellerNipt = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SellerAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SellerIban = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SellerBank = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SellerSwift = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    BuyerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BuyerNipt = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BuyerAddress = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    BuyerEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceSequences",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Last = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSequences", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    Module = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitCents = table.Column<int>(type: "integer", nullable: false),
                    TotalCents = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEmailLog_OwnerId_Kind_PeriodKey",
                table: "BillingEmailLog",
                columns: new[] { "OwnerId", "Kind", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_ProcessedUtc",
                table: "BillingEvents",
                column: "ProcessedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_Provider_EventId",
                table: "BillingEvents",
                columns: new[] { "Provider", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Number",
                table: "Invoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OwnerId_IssuedUtc",
                table: "Invoices",
                columns: new[] { "OwnerId", "IssuedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Status_DueUtc",
                table: "Invoices",
                columns: new[] { "Status", "DueUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SubscriptionId_PeriodStartUtc",
                table: "Invoices",
                columns: new[] { "SubscriptionId", "PeriodStartUtc" },
                unique: true,
                filter: "\"Status\" <> 'Void'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingEmailLog");

            migrationBuilder.DropTable(
                name: "BillingEvents");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "InvoiceSequences");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropColumn(
                name: "NextBranchQuantity",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "NextInterval",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "NextModules",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "InvoiceDueDays",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "OperatorAddress",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "OperatorBank",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "OperatorEmail",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "OperatorIban",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "OperatorName",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "OperatorNipt",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "OperatorSwift",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "RenewalLeadDays",
                table: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "VatPercent",
                table: "PlanSettings");
        }
    }
}

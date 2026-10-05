using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class PlanSettingsInDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangedById",
                table: "PriceBook",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Withdrawn",
                table: "PriceBook",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PlanSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    SeatsPerBranch = table.Column<int>(type: "integer", nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    SignupEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SignupRequireApproval = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanSettings");

            migrationBuilder.DropColumn(
                name: "ChangedById",
                table: "PriceBook");

            migrationBuilder.DropColumn(
                name: "Withdrawn",
                table: "PriceBook");
        }
    }
}

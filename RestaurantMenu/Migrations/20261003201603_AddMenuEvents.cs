using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MenuEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    Lang = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MenuEvents_BranchId_CreatedUtc",
                table: "MenuEvents",
                columns: new[] { "BranchId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuEvents_CreatedUtc",
                table: "MenuEvents",
                column: "CreatedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuEvents");
        }
    }
}

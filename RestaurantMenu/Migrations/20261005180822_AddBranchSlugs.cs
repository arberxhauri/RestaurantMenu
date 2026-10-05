using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchSlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Branches",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BranchSlugAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchSlugAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchSlugAliases_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Slug",
                table: "Branches",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchSlugAliases_BranchId",
                table: "BranchSlugAliases",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchSlugAliases_Slug",
                table: "BranchSlugAliases",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BranchSlugAliases");

            migrationBuilder.DropIndex(
                name: "IX_Branches_Slug",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Branches");
        }
    }
}

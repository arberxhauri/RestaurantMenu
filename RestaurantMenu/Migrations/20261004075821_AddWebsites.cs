using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BranchSites",
                columns: table => new
                {
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Tagline = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TaglineTranslations = table.Column<string>(type: "text", nullable: true),
                    About = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    AboutTranslations = table.Column<string>(type: "text", nullable: true),
                    Instagram = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Facebook = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShowHighlights = table.Column<bool>(type: "boolean", nullable: false),
                    ShowMap = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchSites", x => x.BranchId);
                    table.ForeignKey(
                        name: "FK_BranchSites_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Domains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RenderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Domains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Domains_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Domains_BranchId",
                table: "Domains",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_Host",
                table: "Domains",
                column: "Host",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BranchSites");

            migrationBuilder.DropTable(
                name: "Domains");
        }
    }
}

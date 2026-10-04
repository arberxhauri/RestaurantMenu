using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FeedbackEmailOwner",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "FeedbackEnabled",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "FeedbackGoogleForAll",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoogleReviewUrl",
                table: "Branches",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Feedback",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Contact = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TableNumber = table.Column<int>(type: "integer", nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Feedback_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Feedback_BranchId_CreatedUtc",
                table: "Feedback",
                columns: new[] { "BranchId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Feedback");

            migrationBuilder.DropColumn(
                name: "FeedbackEmailOwner",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FeedbackEnabled",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FeedbackGoogleForAll",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "GoogleReviewUrl",
                table: "Branches");
        }
    }
}

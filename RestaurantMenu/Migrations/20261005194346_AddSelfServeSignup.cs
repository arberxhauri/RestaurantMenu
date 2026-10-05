using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfServeSignup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalNote",
                table: "AspNetUsers",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "AspNetUsers",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "AL");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "AspNetUsers",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingDoneUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingQrUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignupRestaurantName",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignupSource",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Admin");

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SignupSource_ApprovedUtc",
                table: "AspNetUsers",
                columns: new[] { "SignupSource", "ApprovedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SignupSource_ApprovedUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ApprovalNote",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ApprovedUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingDoneUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingQrUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SignupRestaurantName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SignupSource",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "AspNetUsers");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class SearchAndSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedUtc",
                table: "Products",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedUtc",
                table: "Categories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "BusinessType",
                table: "Branches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Branches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cuisine",
                table: "Branches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultLanguage",
                table: "Branches",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleBusinessUrl",
                table: "Branches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Branches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Branches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoDescription",
                table: "Branches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoDescriptionTranslations",
                table: "Branches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoTitle",
                table: "Branches",
                type: "character varying(70)",
                maxLength: 70,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoTitleTranslations",
                table: "Branches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShareImage",
                table: "Branches",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowInSearch",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedUtc",
                table: "Branches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Existing rows: "changed now" (nothing better is known), and the main language that
            // keeps every link as it is today: English where the menu has it, else its first language.
            migrationBuilder.Sql("""UPDATE "Branches" SET "UpdatedUtc" = now();""");
            migrationBuilder.Sql("""UPDATE "Categories" SET "UpdatedUtc" = now();""");
            migrationBuilder.Sql("""UPDATE "Products" SET "UpdatedUtc" = now();""");
            migrationBuilder.Sql("""
                UPDATE "Branches" SET "DefaultLanguage" =
                    CASE WHEN ',' || replace("SupportedLanguages", ' ', '') || ',' LIKE '%,en,%' THEN 'en'
                         ELSE split_part(replace("SupportedLanguages", ' ', ''), ',', 1) END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedUtc",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UpdatedUtc",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "BusinessType",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Cuisine",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "DefaultLanguage",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "GoogleBusinessUrl",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "SeoDescription",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "SeoDescriptionTranslations",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "SeoTitle",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "SeoTitleTranslations",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "ShareImage",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "ShowInSearch",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "UpdatedUtc",
                table: "Branches");
        }
    }
}

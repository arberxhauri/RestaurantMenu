using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class StationDevicePairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // StationDevices was created in BackofficeOperations but nothing ever wrote to it, so
            // TokenHash and Revoked hold no data. Devices now pair with a one-time code instead.
            migrationBuilder.DropIndex(
                name: "IX_StationDevices_TokenHash",
                table: "StationDevices");

            migrationBuilder.DropColumn(
                name: "Revoked",
                table: "StationDevices");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "StationDevices");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "StationDevices",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PairedUtc",
                table: "StationDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PairingCodeHash",
                table: "StationDevices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PairingExpiresUtc",
                table: "StationDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionStamp",
                table: "StationDevices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationDevices_PairingCodeHash",
                table: "StationDevices",
                column: "PairingCodeHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StationDevices_PairingCodeHash",
                table: "StationDevices");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "StationDevices");

            migrationBuilder.DropColumn(
                name: "PairedUtc",
                table: "StationDevices");

            migrationBuilder.DropColumn(
                name: "PairingCodeHash",
                table: "StationDevices");

            migrationBuilder.DropColumn(
                name: "PairingExpiresUtc",
                table: "StationDevices");

            migrationBuilder.DropColumn(
                name: "SessionStamp",
                table: "StationDevices");

            migrationBuilder.AddColumn<bool>(
                name: "Revoked",
                table: "StationDevices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "StationDevices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_StationDevices_TokenHash",
                table: "StationDevices",
                column: "TokenHash",
                unique: true);
        }
    }
}

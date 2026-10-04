using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Guests = table.Column<int>(type: "integer", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartsAtLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    StaffNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CancelledBy = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReminderSentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reservations_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReservationSettings",
                columns: table => new
                {
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SlotMinutes = table.Column<int>(type: "integer", nullable: false),
                    CoversPerSlot = table.Column<int>(type: "integer", nullable: false),
                    MaxPartySize = table.Column<int>(type: "integer", nullable: false),
                    LeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxDaysAhead = table.Column<int>(type: "integer", nullable: false),
                    LastSeatingMinutes = table.Column<int>(type: "integer", nullable: false),
                    AutoConfirm = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderHours = table.Column<int>(type: "integer", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    NotifyOwner = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedDates = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationSettings", x => x.BranchId);
                    table.ForeignKey(
                        name: "FK_ReservationSettings_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BranchId_ClientRequestId",
                table: "Reservations",
                columns: new[] { "BranchId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BranchId_Phone",
                table: "Reservations",
                columns: new[] { "BranchId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BranchId_StartsAtUtc",
                table: "Reservations",
                columns: new[] { "BranchId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PublicId",
                table: "Reservations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_StartsAtUtc",
                table: "Reservations",
                columns: new[] { "Status", "StartsAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "ReservationSettings");
        }
    }
}

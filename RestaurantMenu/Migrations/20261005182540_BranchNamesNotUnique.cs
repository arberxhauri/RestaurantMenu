using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class BranchNamesNotUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Names are free across the platform; links (Branch.Slug) are what must be unique.
            // IF EXISTS: production's tables predate the Postgres baseline.
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Branches_Name";""");

            // Sign-in now locks an account after 5 wrong passwords, which Identity only does
            // for accounts with lockout on. Accounts made outside Identity may have it off.
            migrationBuilder.Sql("""UPDATE "AspNetUsers" SET "LockoutEnabled" = true WHERE NOT "LockoutEnabled";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Branches_Name",
                table: "Branches",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}

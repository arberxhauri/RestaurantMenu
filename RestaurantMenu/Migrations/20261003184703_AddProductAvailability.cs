using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddProductAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing dish starts available. EF's generated default (false) would
            // have marked the whole menu sold out on deploy.
            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "HideSoldOut",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "HideSoldOut",
                table: "Branches");
        }
    }
}

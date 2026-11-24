using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class OrderItemStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "OrderItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Dishes already sent take their station ticket's status, so orders in progress stay where they are.
            migrationBuilder.Sql("""
                UPDATE "OrderItems" AS i SET "Status" = t."Status"
                FROM "OrderTickets" AS t
                WHERE t."OrderId" = i."OrderId" AND t."Station" = i."Station";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "OrderItems");
        }
    }
}

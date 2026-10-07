using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class PaddleEnvironments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PaddleProducts",
                table: "PaddleProducts");

            migrationBuilder.AddColumn<string>(
                name: "ProviderEnvironment",
                table: "Subscriptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaddleEnvironment",
                table: "PriceBook",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                table: "PaddleProducts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "sandbox");

            // Every Paddle id stored so far came from the sandbox (the only account used before
            // this change). Labelled, so a live account ignores them and syncs its own.
            migrationBuilder.Sql("""UPDATE "PriceBook" SET "PaddleEnvironment" = 'sandbox' WHERE "PaddlePriceId" IS NOT NULL;""");
            migrationBuilder.Sql("""UPDATE "Subscriptions" SET "ProviderEnvironment" = 'sandbox' WHERE "Provider" = 'Paddle' OR "ProviderCustomerId" IS NOT NULL;""");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PaddleProducts",
                table: "PaddleProducts",
                columns: new[] { "Module", "Environment" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PaddleProducts",
                table: "PaddleProducts");

            migrationBuilder.DropColumn(
                name: "ProviderEnvironment",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PaddleEnvironment",
                table: "PriceBook");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "PaddleProducts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PaddleProducts",
                table: "PaddleProducts",
                column: "Module");
        }
    }
}

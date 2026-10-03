using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddTableOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderCounter",
                table: "Branches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OrderCounterDay",
                table: "Branches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OrderingEnabled",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OrdersPaused",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Tables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tables_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    TableId = table.Column<int>(type: "integer", nullable: true),
                    TableNumber = table.Column<int>(type: "integer", nullable: false),
                    TableName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    OrderDay = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Orders_Tables_TableId",
                        column: x => x.TableId,
                        principalTable: "Tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Options = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OptionIds = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BranchId_ClientRequestId",
                table: "Orders",
                columns: new[] { "BranchId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BranchId_OrderDay_Number",
                table: "Orders",
                columns: new[] { "BranchId", "OrderDay", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BranchId_Status_CreatedUtc",
                table: "Orders",
                columns: new[] { "BranchId", "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedUtc",
                table: "Orders",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PublicId",
                table: "Orders",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TableId",
                table: "Orders",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_Tables_BranchId_Number",
                table: "Tables",
                columns: new[] { "BranchId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Tables");

            migrationBuilder.DropColumn(
                name: "OrderCounter",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "OrderCounterDay",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "OrderingEnabled",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "OrdersPaused",
                table: "Branches");
        }
    }
}

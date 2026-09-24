using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MilkTeaWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddCartInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    CartId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.CartId);
                    table.ForeignKey(
                        name: "FK_Carts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    CartItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CartId = table.Column<int>(type: "int", nullable: false),
                    ProductSizeId = table.Column<int>(type: "int", nullable: false),
                    SugarLevelId = table.Column<int>(type: "int", nullable: true),
                    IceLevelId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.CartItemId);
                    table.CheckConstraint("CK_CartItem_Quantity_Positive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_CartItems_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "CartId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_IceLevels_IceLevelId",
                        column: x => x.IceLevelId,
                        principalTable: "IceLevels",
                        principalColumn: "IceLevelId");
                    table.ForeignKey(
                        name: "FK_CartItems_ProductSizes_ProductSizeId",
                        column: x => x.ProductSizeId,
                        principalTable: "ProductSizes",
                        principalColumn: "ProductSizeId");
                    table.ForeignKey(
                        name: "FK_CartItems_SugarLevels_SugarLevelId",
                        column: x => x.SugarLevelId,
                        principalTable: "SugarLevels",
                        principalColumn: "SugarLevelId");
                });

            migrationBuilder.CreateTable(
                name: "CartItemToppings",
                columns: table => new
                {
                    CartItemId = table.Column<int>(type: "int", nullable: false),
                    ToppingId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItemToppings", x => new { x.CartItemId, x.ToppingId });
                    table.ForeignKey(
                        name: "FK_CartItemToppings_CartItems_CartItemId",
                        column: x => x.CartItemId,
                        principalTable: "CartItems",
                        principalColumn: "CartItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItemToppings_Toppings_ToppingId",
                        column: x => x.ToppingId,
                        principalTable: "Toppings",
                        principalColumn: "ToppingId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId",
                table: "CartItems",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_IceLevelId",
                table: "CartItems",
                column: "IceLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_ProductSizeId",
                table: "CartItems",
                column: "ProductSizeId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_SugarLevelId",
                table: "CartItems",
                column: "SugarLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItemToppings_ToppingId",
                table: "CartItemToppings",
                column: "ToppingId");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_UserId",
                table: "Carts",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CartItemToppings");

            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "Carts");
        }
    }
}

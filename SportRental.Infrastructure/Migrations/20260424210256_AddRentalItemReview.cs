using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRentalItemReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RentalItemReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalItemReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RentalItemReviews_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RentalItemReviews_RentalItems_RentalItemId",
                        column: x => x.RentalItemId,
                        principalTable: "RentalItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RentalItemReviews_RentalReviews_RentalReviewId",
                        column: x => x.RentalReviewId,
                        principalTable: "RentalReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RentalItemReviews_ProductId",
                table: "RentalItemReviews",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RentalItemReviews_RentalItemId",
                table: "RentalItemReviews",
                column: "RentalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RentalItemReviews_RentalReviewId_RentalItemId",
                table: "RentalItemReviews",
                columns: new[] { "RentalReviewId", "RentalItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RentalItemReviews");
        }
    }
}

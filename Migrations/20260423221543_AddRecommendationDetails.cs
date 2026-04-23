using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace together_api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Duration",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventLocation",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFree",
                table: "Recommendations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Recommendations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Recommendations",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Duration",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "EventLocation",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "IsFree",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Recommendations");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoobackVash.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoPlacesAndTagReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiBoss",
                table: "Photos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiPlace",
                table: "Photos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiTagged",
                table: "Photos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AiTags",
                table: "Photos",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Boss",
                table: "Photos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Place",
                table: "Photos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TagsConfirmed",
                table: "Photos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiBoss",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "AiPlace",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "AiTagged",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "AiTags",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "Boss",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "Place",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "TagsConfirmed",
                table: "Photos");
        }
    }
}

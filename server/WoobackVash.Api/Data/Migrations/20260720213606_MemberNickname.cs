using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoobackVash.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemberNickname : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "Members",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "Members");
        }
    }
}

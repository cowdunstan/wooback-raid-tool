using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoobackVash.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeLootPrioExclusionsPerRaid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LootPrioExclusions_CharacterId_ItemName",
                table: "LootPrioExclusions");

            migrationBuilder.AddColumn<string>(
                name: "Raid",
                table: "LootPrioExclusions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_LootPrioExclusions_CharacterId_Raid_ItemName",
                table: "LootPrioExclusions",
                columns: new[] { "CharacterId", "Raid", "ItemName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LootPrioExclusions_CharacterId_Raid_ItemName",
                table: "LootPrioExclusions");

            migrationBuilder.DropColumn(
                name: "Raid",
                table: "LootPrioExclusions");

            migrationBuilder.CreateIndex(
                name: "IX_LootPrioExclusions_CharacterId_ItemName",
                table: "LootPrioExclusions",
                columns: new[] { "CharacterId", "ItemName" },
                unique: true);
        }
    }
}

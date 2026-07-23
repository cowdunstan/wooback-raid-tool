using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoobackVash.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterImportCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RosterImportCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    RealmSlug = table.Column<string>(type: "text", nullable: true),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Class = table.Column<string>(type: "text", nullable: true),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterImportCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RosterImportCandidates_Name",
                table: "RosterImportCandidates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RosterImportCandidates");
        }
    }
}

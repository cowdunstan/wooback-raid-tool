using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoobackVash.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoRejections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoRejections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UploaderUid = table.Column<string>(type: "text", nullable: false),
                    UploaderName = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    Categories = table.Column<string>(type: "jsonb", nullable: false),
                    Scores = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoRejections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoRejections_CreatedAt",
                table: "PhotoRejections",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoRejections");
        }
    }
}
